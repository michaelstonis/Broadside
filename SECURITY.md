# Security policy

Broadside reads PDF files that nobody has checked: files uploaded by users, attached to email, fetched from the web. Treat a crash, a hang or runaway memory caused by a crafted file as a security problem, and report it privately as described below.

## Supported versions

Broadside follows Semantic Versioning: there are no breaking changes within 1.x. Every package, the viewers included, ships under one shared version number. Broadside is at its 1.0 release candidate. It is maintained on a best-effort basis: there are no guaranteed support windows or response times.

Fixes, security fixes included, ship in the next release. Older releases are not patched: upgrade to the latest release to get a fix. Within 1.x that needs no code changes.

Every 1.x release targets .NET 10 (`net10.0`). Fixes are made on the `main` branch.

## Reporting a vulnerability

Report vulnerabilities privately through GitHub's private vulnerability reporting: open the repository's **Security** tab and choose **Report a vulnerability** (direct link: https://github.com/michaelstonis/Broadside/security/advisories/new). This creates a private draft security advisory that only you and the maintainers can see, and the whole process (discussion, fix, CVE and publication) happens in it.

**Do not open a public issue, discussion or pull request for a security problem.** A public report gives anyone who finds it a working exploit before a fix exists.

Include what you can of the following:

- the affected area (object parser, cross-reference reader, filter or image codec, font program parser, CMap, content interpreter, security handler, signature validation, renderer, and so on);
- a minimal PDF file or byte sequence that triggers the problem, attached to the advisory rather than posted elsewhere;
- the impact you observed (crash, hang, excessive memory or CPU, incorrect decryption, a signature reported valid when it is not, file or network access, information disclosure, code execution);
- the options you opened the file with (for example whether you lowered or raised any limit) and the package version or commit you tested against.

## What happens next

We aim to acknowledge a report within about a week and to confirm or decline it, with a severity (CVSS), soon after. This is a best-effort project, so these are goals, not guarantees.

Fixes are prioritized by severity and ship in a release as soon as they are ready. We keep you updated in the advisory thread, including the expected release once a fix is under way.

## Advisories, CVEs and disclosure

- Every confirmed vulnerability gets a GitHub security advisory in this repository. For any issue of moderate severity or higher we request a CVE identifier through GitHub, which is a CVE Numbering Authority, from the same advisory.
- The advisory is published when the fixed packages are on NuGet.org, and it lists the affected and fixed versions so that `dotnet list package --vulnerable` and Dependabot alerts pick it up. The release notes of the fixed version link to it.
- We follow coordinated disclosure: please keep the details private until the advisory is published, or until 90 days after your report, whichever comes first. If a fix needs longer, we will ask you in the advisory thread. If a vulnerability is already public or actively exploited, we publish as soon as a fix is available.
- We credit you in the advisory unless you prefer otherwise.

## Scope

Broadside parses untrusted input by design. The following are in scope and are treated as security issues, not ordinary bugs:

- **Crashes, hangs or unbounded memory or CPU use caused by a malformed PDF**, including malformed content streams, cross-reference data, fonts, CMaps, images, filters, ICC profiles, functions, shadings and encryption dictionaries. A parser that throws an unhandled exception, loops forever, recurses until the stack overflows, or exhausts memory on crafted input is a denial-of-service vulnerability. Decoding is bounded by the documented resource limits; a crafted file that gets past them is in scope.
- Memory-safety issues in any code using `unsafe`, `Span<T>` slicing or `ArrayPool<T>` buffers.
- Incorrect behavior of the security handlers (encryption, decryption, permissions) or of signature validation that would let a document appear valid, unmodified or signed by someone it was not.
- Any path by which a document could cause code or script execution. JavaScript, XFA, rich media and similar features are read and preserved as data only and must never execute.
- Any file or network access caused by a document beyond what the application turned on: Broadside reads no external file a document names and makes no network request unless the application passes it an `HttpClient`.

Out of scope:

- Vulnerabilities in third-party backends and platform code that Broadside merely calls (SkiaSharp, CoreGraphics and ImageIO, Direct2D and WIC, Android graphics, the browser): report those upstream. Tell us too if Broadside should guard against them.
- Resource use within the documented limits: a file that decodes to the full `MaxDecodedLength`, or a page rendered at the size the application asked for, is working as designed. Lower the limits for untrusted input as [Processing untrusted files](https://michaelstonis.github.io/Broadside/articles/untrusted-files/) explains.
- Findings from automated scanners with no demonstrated impact; they are triaged as ordinary issues.

The threat model, with every entry point and every resource limit, its default, the option that changes it and the test that proves it, is in [docs/threat-model.md](docs/threat-model.md).
