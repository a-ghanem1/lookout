# Security Policy

## Supported versions

Security fixes are applied to the latest released version of each Lookout package on NuGet. Older preview/prerelease versions are not patched — please upgrade to the latest release before reporting.

| Package                         | Supported     |
| ------------------------------- | ------------- |
| `Lookout.AspNetCore` (latest)   | ✅            |
| `Lookout.Hangfire` (latest)     | ✅            |
| Other Lookout.* packages (latest) | ✅          |
| Older preview versions          | ❌            |

## Reporting a vulnerability

**Please do not open a public GitHub issue for security vulnerabilities.**

Report privately via one of:

1. **GitHub private vulnerability advisory** (preferred) — open one at
   https://github.com/a-ghanem1/Lookout/security/advisories/new
2. **Email** — `a.ghanem2244@gmail.com` with subject `[Lookout security]`.

Please include:

- Affected package(s) and version(s).
- A description of the issue and its impact.
- Steps to reproduce, ideally a minimal repro project or failing test.
- Any suggested fix or mitigation, if you have one.

## What to expect

- **Acknowledgement** within 72 hours.
- **Initial assessment** (severity, scope, affected versions) within 7 days.
- **Fix and coordinated disclosure** — we'll work with you on a disclosure timeline appropriate to severity. Critical issues get a patched release as soon as a fix is verified; lower-severity issues may be bundled into the next scheduled release.
- **Credit** — reporters are credited in the release notes and advisory unless they prefer to remain anonymous.

## Scope

In scope:

- Vulnerabilities in any Lookout NuGet package or the embedded dashboard UI.
- Issues that allow Lookout to be enabled in production unintentionally, leak captured data outside the host process, or expose the dashboard to unauthenticated external access.

Out of scope:

- Findings that require an attacker to already have local code execution or filesystem access on the host machine (Lookout is a dev-time tool — that threat model is the developer's machine).
- Issues in third-party dependencies — please report those upstream. We'll update the affected dependency once a fix is available.
- Theoretical vulnerabilities without a working proof-of-concept.

Thank you for helping keep Lookout and its users safe.
