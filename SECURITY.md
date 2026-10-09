# Security policy

## Supported versions

No version of Broadside has been released yet, so no version is currently supported with security fixes. Once packages are published, this table will list the versions that receive fixes.

| Version | Supported |
|---|---|
| none yet | – |

## Reporting a vulnerability

Report vulnerabilities privately through GitHub's private vulnerability reporting: open the repository's **Security** tab and choose **Report a vulnerability** (direct link: https://github.com/michaelstonis/Broadside/security/advisories/new). This creates a private draft advisory that only you and the maintainers can see.

**Do not open a public issue, discussion or pull request for a security problem.** A public report gives anyone who finds it a working exploit before a fix exists.

Include what you can of the following:

- the affected area (parser, filter, font program parser, image codec, security handler, renderer, and so on);
- a minimal PDF file or byte sequence that triggers the problem, attached to the advisory rather than posted elsewhere;
- the impact you observed (crash, hang, excessive memory, incorrect decryption, information disclosure, code execution);
- the commit or package version you tested against.

## Response

You will receive an acknowledgement within 7 days of the report. After that we will keep you updated in the advisory thread as we confirm the issue, develop a fix and plan disclosure. We ask that you give us a reasonable window to ship a fix before disclosing publicly, and we will credit you in the advisory unless you prefer otherwise.

## Scope

Broadside parses untrusted input by design. The following are in scope and are treated as security issues, not ordinary bugs:

- **Crashes, hangs or unbounded memory use caused by a malformed PDF**, including malformed content streams, cross-reference data, fonts, images, filters and encryption dictionaries. A parser that throws an unhandled exception, loops forever or exhausts memory on crafted input is a denial-of-service vulnerability.
- Memory-safety issues in any code using `unsafe`, `Span<T>` slicing or `ArrayPool<T>` buffers.
- Incorrect behavior of the security handlers (encryption, decryption, permissions) or signature validation that would let a document appear valid when it is not.
- Any path by which a document could cause code or script execution. JavaScript, XFA, rich media and similar features are parse-and-preserve only and must never execute.

Out of scope: vulnerabilities in third-party backends (SkiaSharp, platform graphics APIs) that Broadside merely calls; report those upstream. Findings from automated scanners with no demonstrated impact will be triaged as ordinary issues.
