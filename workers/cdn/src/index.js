/**
 * CodePush CDN Worker — Monkseal
 *
 * Proxies download requests to Azure Blob Storage with authentication.
 * Validates the X-CodePush-Token header against the CodePush server.
 *
 * URL format: https://cdn.monkseal.dev/{container}/{blobPath}
 * Example:    https://cdn.monkseal.dev/patches/appId/patches/patchId.dll
 *
 * Required secrets (set via `wrangler secret put`):
 *   AZURE_STORAGE_KEY    — Azure Blob Storage account key
 *   CODEPUSH_SERVER_URL  — CodePush API URL for token validation
 */

export default {
  async fetch(request, env) {
    // Handle CORS preflight
    if (request.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: corsHeaders() });
    }

    const url = new URL(request.url);
    const path = url.pathname.slice(1); // remove leading /

    if (!path) {
      return new Response(JSON.stringify({ error: "Not found" }), {
        status: 404,
        headers: corsHeaders("application/json"),
      });
    }

    // Parse: /{container}/{blobPath...}
    const slashIdx = path.indexOf("/");
    if (slashIdx === -1) {
      return new Response(JSON.stringify({ error: "Invalid path" }), {
        status: 400,
        headers: corsHeaders("application/json"),
      });
    }

    const container = path.substring(0, slashIdx);
    const blobPath = path.substring(slashIdx + 1);

    // Validate token
    const token = request.headers.get("X-CodePush-Token");
    if (!token) {
      return new Response(JSON.stringify({ error: "Missing X-CodePush-Token header" }), {
        status: 401,
        headers: corsHeaders("application/json"),
      });
    }

    // Extract appId from blob path (format: {appId}/patches/{id}.dll or {appId}/releases/{id}/{module}.dll)
    const appId = blobPath.split("/")[0];
    if (!appId) {
      return new Response(JSON.stringify({ error: "Invalid blob path" }), {
        status: 400,
        headers: corsHeaders("application/json"),
      });
    }

    // Validate token against CodePush server
    const validation = await validateToken(env, appId, token);
    if (validation === "error") {
      return new Response(JSON.stringify({ error: "Token validation unavailable" }), {
        status: 502,
        headers: corsHeaders("application/json"),
      });
    }
    if (!validation) {
      return new Response(JSON.stringify({ error: "Invalid or unauthorized token" }), {
        status: 403,
        headers: corsHeaders("application/json"),
      });
    }

    // Fetch from Azure Blob Storage
    const storageAccount = env.AZURE_STORAGE_ACCOUNT || "stmonksealcodepush";
    const storageKey = env.AZURE_STORAGE_KEY;

    if (!storageKey) {
      return new Response(JSON.stringify({ error: "Storage not configured" }), {
        status: 500,
        headers: corsHeaders("application/json"),
      });
    }

    const blobUrl = `https://${storageAccount}.blob.core.windows.net/${container}/${blobPath}`;
    const now = new Date().toUTCString();

    // Build Azure Blob Storage authorization header (Shared Key)
    const authHeader = await buildAzureAuthHeader(
      storageAccount,
      storageKey,
      "GET",
      container,
      blobPath,
      now
    );

    const blobResponse = await fetch(blobUrl, {
      headers: {
        Authorization: authHeader,
        "x-ms-version": "2021-08-06",
        "x-ms-date": now,
      },
    });

    if (!blobResponse.ok) {
      const status = blobResponse.status === 404 ? 404 : 502;
      return new Response(
        JSON.stringify({ error: status === 404 ? "File not found" : "Storage error" }),
        { status, headers: corsHeaders("application/json") }
      );
    }

    // Stream the blob to the client with caching headers
    const responseHeaders = new Headers({
      "Content-Type": "application/octet-stream",
      "Cache-Control": "public, max-age=86400, immutable",
      ...corsHeaders(),
    });

    const contentLength = blobResponse.headers.get("Content-Length");
    if (contentLength) responseHeaders.set("Content-Length", contentLength);
    const etag = blobResponse.headers.get("ETag");
    if (etag) responseHeaders.set("ETag", etag);

    const fileName = blobPath.split("/").pop();
    if (fileName) {
      responseHeaders.set("Content-Disposition", `attachment; filename="${fileName}"`);
    }

    return new Response(blobResponse.body, {
      status: 200,
      headers: responseHeaders,
    });
  },
};

/**
 * Validate X-CodePush-Token by checking against the CodePush server.
 * Caches valid tokens for 5 minutes to reduce server load.
 */
const tokenCache = new Map();

async function validateToken(env, appId, token) {
  const cacheKey = `${appId}:${token}`;
  const cached = tokenCache.get(cacheKey);
  if (cached && cached.expiry > Date.now()) {
    return cached.valid;
  }

  try {
    const serverUrl = env.CODEPUSH_SERVER_URL || "https://codepush.monkseal.dev";
    const response = await fetch(
      `${serverUrl}/api/updates/check?app=${encodeURIComponent(appId)}&releaseVersion=__validate__&platform=any`,
      { headers: { "X-CodePush-Token": token } }
    );

    // 200 = valid token (even if no updates), 401 = invalid
    const valid = response.status === 200;

    tokenCache.set(cacheKey, { valid, expiry: Date.now() + 5 * 60 * 1000 });

    // Evict old entries
    if (tokenCache.size > 10000) {
      const oldest = tokenCache.keys().next().value;
      tokenCache.delete(oldest);
    }

    return valid;
  } catch {
    // Network/server error — don't cache, return "error" to distinguish from invalid token
    return "error";
  }
}

/**
 * Build Azure Blob Storage Shared Key authorization header.
 * https://learn.microsoft.com/en-us/rest/api/storageservices/authorize-with-shared-key
 */
async function buildAzureAuthHeader(account, key, method, container, blob, date) {
  const stringToSign = [
    method,           // HTTP verb
    "",               // Content-Encoding
    "",               // Content-Language
    "",               // Content-Length
    "",               // Content-MD5
    "",               // Content-Type
    "",               // Date
    "",               // If-Modified-Since
    "",               // If-Match
    "",               // If-None-Match
    "",               // If-Unmodified-Since
    "",               // Range
    // Canonicalized headers
    `x-ms-date:${date}\nx-ms-version:2021-08-06`,
    // Canonicalized resource
    `/${account}/${container}/${blob}`,
  ].join("\n");

  const keyBytes = Uint8Array.from(atob(key), (c) => c.charCodeAt(0));
  const cryptoKey = await crypto.subtle.importKey(
    "raw",
    keyBytes,
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );

  const signatureBytes = await crypto.subtle.sign(
    "HMAC",
    cryptoKey,
    new TextEncoder().encode(stringToSign)
  );

  const signature = btoa(String.fromCharCode(...new Uint8Array(signatureBytes)));
  return `SharedKey ${account}:${signature}`;
}

function corsHeaders(contentType) {
  const headers = {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Methods": "GET, OPTIONS",
    "Access-Control-Allow-Headers": "X-CodePush-Token, Content-Type",
  };
  if (contentType) headers["Content-Type"] = contentType;
  return headers;
}
