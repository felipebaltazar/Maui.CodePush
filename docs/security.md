# Security

How CodePush protects your code and your users.

---

## Authentication Model

CodePush uses three layers of authentication:

| Layer | Method | Purpose |
|-------|--------|---------|
| **Developer** | JWT token or API Key | Publishing releases and patches |
| **Mobile App** | App Token | Checking and downloading updates |
| **Browser Login** | Device Authorization Flow | CLI authentication via browser |

### Developer Authentication

- **JWT Token**: Obtained via `codepush login` (browser-based). Expires in 7 days.
- **API Key**: Permanent key for CI/CD pipelines. Sent via `X-Api-Key` header. Obtained from account settings.

### App Authentication

Each app gets a unique **App Token** when registered. This token:
- Is embedded in your mobile app
- Sent via `X-CodePush-Token` header on update checks
- Can be revoked and regenerated
- Is not a secret (can be decompiled from the app) but prevents casual API abuse

### Browser Login Flow

The CLI uses the [Device Authorization Flow](https://datatracker.ietf.org/doc/html/rfc8628):

1. CLI requests a one-time code (e.g., `KXPT-3NV7`)
2. Opens browser to the verification page
3. User logs in and approves the code
4. CLI receives credentials automatically

No password is ever entered in the terminal.

## App Ownership

App registration is **first-come-first-served** by package name. Once registered, only the account owner can publish updates. This is the same model used by Shorebird.

## Integrity Verification

Every DLL uploaded to CodePush is:
- Hashed with **SHA-256** at upload time
- Hash stored in the database
- Verified by the mobile app after download
- Rejected if hash doesn't match

## Dependency Compatibility

Before a patch is accepted, the CLI compares assembly references against the release snapshot. This prevents:
- Loading assemblies not present in the app binary
- Using newer versions of assemblies than what shipped

## Transport Security

- All traffic uses **HTTPS** (TLS 1.2+)
- Cloudflare provides DDoS protection and edge caching
- Azure Blob Storage is private (no public access)
- Downloads go through CDN with token validation

## App Store Compliance

### Apple iOS (App Store Guidelines 3.3.2)

The Mono interpreter is part of the app binary (shipped inside the .NET runtime). Interpreting IL bytecode is analogous to JavaScript in WebKit. This is the same approach used by React Native CodePush and Shorebird.

CodePush configures `MtouchInterpreter` to enable interpretation **only for CodePush modules**, keeping the rest of your app at full AOT performance.

### Google Play

.NET assemblies run on the Mono runtime virtual machine, which qualifies under Google's policy for VM-based code execution.

### Best Practices

- Use CodePush for **hotfixes and minor updates** only
- Don't use it to circumvent app store review for major feature additions
- Don't change the primary purpose of your app via code push
