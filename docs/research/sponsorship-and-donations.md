# Sponsorship and Donations: How Funded Open-Source Projects Structure It

*Research memo, November 2026. All pages were fetched on 2026-11-24. Sponsor counts, balances and budgets are snapshots from that day. Every claim links to its source. Where a source is secondary (press, aggregator, reseller), it is flagged. Web content was treated as data, not instructions.*

Context: Broadside is MIT licensed, and [ADR 0008](../adr/0008-mit-license-sponsorship-not-restriction.md) settles that revenue comes from sponsorship tied to support and prioritization, never from the license. [SPONSORS.md](../../SPONSORS.md) already sketches three unpriced tiers (Individual, Company, Enterprise). This memo collects evidence for pricing and wording them. Sections 1 to 5 are sourced findings. Section 6 is the recommendation. Section 7, added later, benchmarks the Company tier price against .NET PDF SDK pricing and ends with its own recommendation. Section 8 records what the owner decided, which differs from both recommendations; [SPONSORS.md](../../SPONSORS.md) is the public version.

## 1. Exemplars

### 1.1 Donation-funded projects outside .NET

**Vue.js (Evan You), GitHub Sponsors.** Ten monthly tiers, no one-time tiers. Custom amounts are allowed, and anything from $5 to $50 counts as a Backer ([github.com/sponsors/yyx990803](https://github.com/sponsors/yyx990803)). 140 current sponsors, 1,247 past sponsors.

| Tier | $/month | Promise |
|---|---|---|
| Backer | 5 | Name in vuejs/core README and BACKERS |
| Generous Backer | 50 | Name in a separate "Generous Backers" section |
| Bronze | 100 | Company name or small logo in README and BACKERS |
| Silver | 250 | Logo on the vuejs.org sponsor page plus READMEs |
| Gold | 500 | Logo on the vuejs.org front page (500k+ monthly views), or Vite docs |
| Dual Gold | 1,000 | Gold placement on both Vue and Vite |
| Partner: Gold / Platinum | 1,001 / 2,001 | Listing on vuejs.org/partners, prior approval required |
| Platinum | 2,000 | Logo in the sidebar of every vuejs.org content page (3M+ monthly views) |
| Diamond | 4,000 | Platinum placement plus Vite front page and a shoutout from the Vue account |

Every Vue tier sells **visibility** (docs traffic), never support. The [vuejs.org sponsor page](https://vuejs.org/sponsor/) says "business sponsorships typically make a much larger impact on the sustainability of OSS projects than individual donations" and that "Invoices can be obtained via GitHub's payment system." Evan You's newer toolchain work moved to a VC-funded company: VoidZero raised a $4.6M seed led by Accel in October 2024, because the vision "wasn't possible under the independent sustainability model of my past projects" ([voidzero.dev](https://voidzero.dev/posts/announcing-voidzero-inc)).

**Vite** ([github.com/sponsors/vitejs](https://github.com/sponsors/vitejs)): Backer $5, Supporter $15, Bronze $50, Silver $150 (thanks only), Gold $500 (logo in docs), Platinum $2,000 (prominent logo in docs). 44 current sponsors. **Vitest** ([github.com/sponsors/vitest-dev](https://github.com/sponsors/vitest-dev)): $5, $15, $50 (thanks only), then Medium $100 and Large $500 (logo on vitest.dev and the README). 19 current sponsors.

**Babel.** Open Collective tiers: Backers from $2/mo, Bronze $100/mo ("recognition"), Silver $500/mo (logo on babeljs.io), Gold $1,000/mo (large logo), and Base Support Sponsor at $24,000/yr (logo plus support access) ([opencollective.com/babel](https://opencollective.com/babel)). Its GitHub Sponsors page adds Base Support at $2,000/mo for "2 hours of dedicated support per month" and Company Core Sponsor at $4,000/mo, which funds one core member part time ([github.com/sponsors/babel](https://github.com/sponsors/babel)). Babel is the cautionary case. All-time raised on Open Collective is $1.66M, with engineering as the largest expense category ($1.04M) ([opencollective.com/babel](https://opencollective.com/babel)). In 2021 the team said it needed "at least $333,000 per year, which is 2x what we're currently bringing in." It paid a baseline of $11,000/month for full-time work and $2,000/month part time, and warned that many small contributions create "a false sense of sustainability" ([Babel blog, 2021](https://babeljs.io/blog/2021/05/10/funding-update)). By the Babel 8 release, recurring donations had fallen from about $150k/yr (2018 to 2022) to about $50k in 2025 and about $12k in 2026. The team said it could not have kept its quality bar without the Sovereign Tech Agency, whose support is "scheduled to end soon", and Igalia ([Babel 8 post](https://babeljs.io/blog/2026/06/16/8.0.0)). The GitHub Sponsors goal of $12,000/month showed 1% progress ([github.com/sponsors/babel](https://github.com/sponsors/babel)).

**webpack.** Only two tiers: Backer from $2/mo and Sponsor from $200/mo, with no perks listed. Estimated annual budget $171k, $1.78M raised all-time, 3,252 contributors, hosted by Open Source Collective ([opencollective.com/webpack](https://opencollective.com/webpack)).

**curl (Daniel Stenberg).** Open Collective tiers: Backer $5, Supporter $25, Silver $100, Gold $500, Platinum $1,000/mo. The sponsor tiers only promise listing on curl's sponsor page, and the page says contributions are donations with no goods or services in return ([opencollective.com/curl](https://opencollective.com/curl)). Budget about $91k/yr, $439k raised all-time. Donations "mostly" pay project expenses, chiefly the bug bounty. Stenberg's salary comes from wolfSSL, which sells curl support contracts: "Companies pay wolfSSL, wolfSSL pays me a salary" ([curl sponsors page](https://curl.se/sponsors.html), [daniel.haxx.se wolfSSL tag](https://daniel.haxx.se/blog/tag/wolfssl/)). His GitHub Sponsors tiers ($5, $6, $25, $100, $1,000) promise nothing beyond jokes and a badge, and show 67 current sponsors ([github.com/sponsors/bagder](https://github.com/sponsors/bagder)).

**Homebrew.** This is the only large project here that pairs recurring tiers with **one-time 12-month equivalents**, which suits companies that cannot hold a subscription ([opencollective.com/homebrew](https://opencollective.com/homebrew)):

| Tier | Recurring | One-time (12 months) | Promise |
|---|---|---|---|
| Supporter | from $10/mo | from $120 | Priority help in issues, discussions and PRs when you say you sponsor |
| Sponsor | from $250/mo | from $3,000 | Plus name in plain text in the README |
| Lead Sponsor | from $1,000/mo | from $12,000 | Plus linked image in the README and thanks in release posts |

Budget about $102k/yr, $473k raised, balance $130k.

**Astro.** Individual $10, Sponsor $100 (README logo), Theme Sponsor $200, Gold $250 (homepage logo), Platinum $1,000, Exclusive $10,000/mo (custom partnership). Budget $434k/yr, $996k raised ([opencollective.com/astrodotbuild](https://opencollective.com/astrodotbuild)).

**Sindre Sorhus** ([github.com/sponsors/sindresorhus](https://github.com/sponsors/sindresorhus)): $5 and $10 (badge), $50 (name on his thanks page), $100 (logo on one repo of your choice), $200 (two repos), $800 (Awesome Node.js and Awesome Electron), $1,000 (main Awesome list, about 60k monthly views). The rules are strict: no text under logos, no UTM parameters, and no gambling companies. 187 current sponsors, 1,995 past. Like Vue, he sells placement against measured traffic.

**Caleb Porzio (Livewire, Alpine).** His GitHub Sponsors tiers are $14 and $29 for individuals (both include "Sponsorware" and access to screencasts) and $250 and $500 for businesses, which list no perks at all. 94 current, 3,373 past ([github.com/sponsors/calebporzio](https://github.com/sponsors/calebporzio)). He reached $112,680/yr in June 2020 after "sponsorware" added about $11k/yr "in a matter of days" ([calebporzio.com, 2020](https://calebporzio.com/i-just-hit-dollar-100000yr-on-github-sponsors-heres-how-i-did-it)). His August 2024 breakdown of passing $1M on GitHub Sponsors shows that this was **sales through the Sponsors checkout, not donations**: $725k from premium Livewire screencasts, $200k from companies buying logo placement, $25k consulting, $20k early access to a side project, and only $10k from "buy me coffee" tips and stickers. His advice: "SELL THEM SOMETHING", "Platform risk is a real thing: DIVERSIFY!" (GitHub dropped PayPal with a month's notice and cost him about $4k/mo), "TURN OFF GITHUB ISSUES", and avoid Discord because it drains deep-work time ([calebporzio.com, 2024](https://calebporzio.com/i-just-cracked-1-million-on-github-sponsors-heres-my-playbook)). GitHub confirms PayPal support ended on February 23, 2023 ([GitHub changelog](https://github.blog/changelog/2023-01-23-github-sponsors-will-stop-supporting-paypal/)).

**Excalidraw.** The Open Collective has only Backer $5 and Sponsor $100, a $2,099 annual budget and $38.6k raised ([opencollective.com/excalidraw](https://opencollective.com/excalidraw)). The real revenue is the Excalidraw+ SaaS at $6/user/month ([plus.excalidraw.com/pricing](https://plus.excalidraw.com/pricing)).

**Tailwind.** No public price list. Partners are asked to email for a tailored package, with benefits such as a Slack channel, calls and "a prioritized bug fix" ([tailwindcss.com/sponsor](https://tailwindcss.com/sponsor)). The business ran on one-time sales of commercial UI kits. In January 2026, Adam Wathan disclosed in a GitHub comment that 75% of the engineering team had been laid off because of "the brutal impact AI has had on our business," as docs traffic (the funnel to paid products) fell (*secondary*: [Simon Willison](https://simonwillison.net/2026/Jan/7/adam-wathan/), [devclass](https://www.devclass.com/ai-ml/2026/01/08/tailwind-labs-lays-off-75-percent-of-engineers-thanks-to-brutal-impact-of-ai/4079571)). The lesson for Broadside is that a funnel dependent on docs page views is fragile.

**Foundations.** The Rails Foundation has Core members at $75,000/yr, each getting a board seat, and Contributing members at $25,000/yr ([rubyonrails.org/foundation](https://rubyonrails.org/foundation)). Ruby Together folded into Ruby Central, its memberships carrying over ([Ruby Central](https://rubycentral.org/news/ruby-together-and-ruby-central--coming-together/)). The Django Software Foundation's corporate levels are Platinum $30,000+, Gold $13,750+, Silver $5,500+ and Bronze $2,200+ per year. Individual presets run from $25 to $2,500. Its 2026 goal was $500k, with $324k raised (64%) and behind pace ([djangoproject.com/fundraising](https://www.djangoproject.com/fundraising/)).

**Filippo Valsorda (Go crypto).** A retainer model "shaped like enterprise sales": a prospectus, a contract and monthly invoices paid by wire. Silver, gold and platinum tiers are all "in the five-figures range (USD, yearly)". Clients get access (roadmap feedback, quarterly meetings, advice at the top tier), not deliverables. He says his income is equivalent to his former Google compensation and that the model "generally looks like a vendor relation, not like a GitHub Sponsors donation." He argues feature-scoped sponsorships "reward increasing future maintenance burden without funding it" ([words.filippo.io](https://words.filippo.io/full-time-maintainer/)).

### 1.2 .NET

| Project | Model | Numbers |
|---|---|---|
| **AvaloniaUI** | MIT core plus paid tooling and controls | Open Collective budget about $1.4k/yr, $36k raised all-time, and it promises "no additional benefits" ([OC](https://opencollective.com/avalonia)). GitHub Sponsors: five unnamed tiers from $6 to $59, perks limited to a readme.txt name and a tweet, 26 current sponsors ([GH](https://github.com/sponsors/AvaloniaUI)). Real revenue: Plus €299 and Pro €899 per seat per year, Enterprise from €21,000/yr with private support (1 working day, 8 hours for critical), plus XPF ([avaloniaui.net/pricing](https://avaloniaui.net/pricing)) |
| **Uno Platform** | Apache 2.0 core plus paid Studio | Studio Pro $39 and Business $119 per developer per month. Purchase orders and invoicing are paid-tier features. OSS support for the framework is sold separately ([platform.uno/pricing](https://platform.uno/pricing/)) |
| **ReactiveUI** | Open Collective only | $15/mo through $500/mo tiers. The $50 tier promises "regular 1:1 calls with maintainers". Gold $12,000 and Platinum $30,000 one-time. Estimated budget $211/yr, $21k raised all-time ([OC](https://opencollective.com/reactiveui)) |
| **AutoMapper / MediatR** | Moved to RPL-1.5 plus commercial license (Lucky Penny Software) | Standard (1 to 10 devs), Professional (11 to 50), Enterprise (unlimited). Free Community edition under $5M revenue. License key logs a message but never blocks. Paddle handles payment ([jimmybogard.com](https://www.jimmybogard.com/automapper-and-mediatr-commercial-editions-launch-today/)) |
| **FluentAssertions** | v8 moved to a proprietary Xceed license | $129.95 per developer per year. v7 stays Apache 2.0 "indefinitely". Backlash and the AwesomeAssertions fork followed (*secondary*: [InfoQ](https://www.infoq.com/news/2025/01/fluent-assertions-v8-license)) |
| **ImageSharp (Six Labors)** | Split License: free under $1M revenue, commercial above | Boutique $799/yr (up to 10 devs), Agency $1,299 (11 to 20), Enterprise $4,999 (unlimited) ([sixlabors.com/pricing](https://sixlabors.com/pricing/)). Since September 2025 a build-time license file is required, because download stats showed "only a small share of commercial users were compliant" ([Six Labors](https://sixlabors.com/posts/licence-enforcement-changes/)) |
| **QuestPDF** | Community License (source-available, not OSI) plus Professional and Enterprise | See below |
| **Hangfire** | LGPL core plus commercial Pro | Startup $500, Business $1,500, Enterprise $4,500 per organization per year. Perpetual, with yearly maintenance. Invoice and purchase order only on Enterprise ([hangfire.io/pricing](https://www.hangfire.io/pricing/)) |
| **Duende IdentityServer** | Commercial, free for development | Lite $480/mo, Standard $1,042/mo, Advanced $2,075/mo (billed annually). Support ladder: community forum, then 2 business days with 2 or 4 escalation calls per year, then 1 business day with a TAM ([duendesoftware.com/pricing](https://duendesoftware.com/pricing)) |
| **.NET Foundation** | Corporate sponsorship of the foundation | No prices published, and no stated pass-through funding for member projects ([dotnetfoundation.org](https://dotnetfoundation.org/membership/corporate-sponsorship)) |

**QuestPDF in detail** (Broadside's closest .NET peer, since it is also a PDF library). The Community License is free for individuals and companies under USD 1,000,000 annual gross revenue, for OSI-licensed open source, charities and academia. Public-sector entities and listed companies are excluded "regardless of revenue". It is enforced by "good-faith self-certification", with no routine audits ([license guide](https://questpdf.com/license/guide.html)). Professional costs $1,999 and covers one legal entity with unlimited developers and direct email support. Enterprise costs $4,999, covers affiliates and adds next-business-day priority email, 12 months' notice before end-of-life, purchase orders, direct invoicing, supplier onboarding and multi-year terms. Both are perpetual, with the first year of updates included and renewal at a locked price. Paddle is merchant of record, and SoftwareOne, Insight, SHI and QBS resell ([pricing](https://questpdf.com/pricing.html)). Meanwhile its GitHub Sponsors page (tiers $5 to $1,000, with "Prioritized support" at $100) shows **4 current sponsors** ([github.com/sponsors/QuestPDF](https://github.com/sponsors/QuestPDF)). The same buyers who will not donate do pay $2k to $5k when it is framed as a license with an invoice.

**Moq and SponsorLink: what not to do.** Moq 4.20 (August 2023) shipped SponsorLink, a closed-source, obfuscated component that read the developer's git email at build time, hashed it and checked it against GitHub Sponsors through a remote service. It shipped with no prior announcement. Maintainer issue #1374 drew heavy backlash over privacy and supply-chain concerns, and 4.20.2 removed it ([moq issue #1374](https://github.com/moq/moq/issues/1374); *secondary*: [Snyk](https://snyk.io/de/blog/moq-package-exfiltrates-user-emails/)). Lesson: never put sponsorship checks, telemetry or nags into the build or runtime of an MIT package.

## 2. Price points and tier structure

**Platform limits.** GitHub allows up to 10 monthly and 10 one-time tiers, with a maximum of US$12,000 per month. A published tier's price cannot be edited: you retire it and create a new one ([GitHub docs](https://docs.github.com/en/sponsors/receiving-sponsorships-through-github-sponsors/managing-your-sponsorship-tiers)).

**Observed price points:**

| Band | Typical amounts | Examples |
|---|---|---|
| Individual entry | $5/mo (sometimes $2, $6, $10) | Vue, Vite, Vitest, curl, Sindre, QuestPDF, Avalonia |
| Individual upper | $14 to $50/mo | Caleb $14/$29, Vite $15, curl $25, Vue $50 |
| Company entry | $100 to $250/mo | Vue Bronze $100, Babel Bronze $100, curl Silver $100, Homebrew Sponsor $250, Caleb $250 |
| Company mid | $500/mo | Vue Gold, Vite Gold, Vitest Large, Babel Silver, curl Gold, Caleb |
| Company top | $1,000 to $4,000/mo | Babel Gold $1,000 and Core $4,000, Vue Platinum $2,000 and Diamond $4,000, Vite Platinum $2,000 |
| Foundation or retainer | $2,200 to $75,000/yr | DSF, Rails, Valsorda (five figures/yr) |

**Number of tiers.** Most successful pages publish 5 to 7 tiers. Vue publishes 10. webpack and Excalidraw get by with 2. Steps are typically 2x to 5x: 5, 15 or 25, 50, 100, 250 or 500, 1,000, 2,000.

**Most-subscribed tiers** (Open Collective's per-tier contributor lists are cumulative "latest activity", so treat them as all-time):

- curl: Backer $5 66, **Silver $100 80**, Gold $500 1, Platinum $1,000 1, custom donations 994 ([curl/contribute](https://opencollective.com/curl/contribute)).
- Babel: Backers $2+ about 524, **Bronze $100 about 56, Silver $500 about 49**, Gold $1,000 about 9, custom about 1,262 ([babel/contribute](https://opencollective.com/babel/contribute)).
- Homebrew: **Supporter $10+ 78**, Sponsor $250+ 4, Lead $1,000+ 0, and zero takers on every one-time 12-month tier ([homebrew/contribute](https://opencollective.com/homebrew/contribute)).

The pattern: one-off custom donations are the most numerous. Among recurring tiers, the cheapest company tier ($100 to $500) is where companies cluster, and tiers at $1,000+ hold a handful or none. GitHub reports that in 2022 organizations supplied nearly 40% of Sponsors funding, with the average organization sponsorship nearly 15x the average individual one ([GitHub blog, July 2026](https://github.blog/open-source/maintainers/100-million-for-open-source-a-milestone-built-by-the-community/)).

## 3. Benefits promised, and which ones cost the maintainer

| Benefit | Who offers it | Cost to the maintainer |
|---|---|---|
| Name in README or SPONSORS file | Nearly everyone | Negligible. Can be automated |
| Logo by size and placement, priced against page views | Vue, Sindre, Vite, Astro, Babel | Low. Sindre's rules (no text, no UTM, category exclusions) prevent haggling. Value depends on docs traffic, which the Tailwind case shows is falling |
| Priority triage or help in issues | Homebrew ($10+), QuestPDF ($100) | Medium. Open-ended unless capped |
| Fixed support hours | Babel ($2,000/mo for 2 hours) | Bounded and explicit. The cleanest way to sell support through Sponsors |
| 1:1 calls | ReactiveUI ($50/mo) | High relative to price. A $50 tier with recurring calls is underpriced labor |
| Private Discord or Slack | Tailwind partners | High. Caleb calls community chat a drain on deep work |
| Response-time SLA | Duende, Avalonia Enterprise, QuestPDF Enterprise | High. Only sold at $5k to $25k+/yr, inside a contract |
| Roadmap input | Valsorda (feedback), Rails (board seat at $75k) | Low as feedback. Dangerous as a binding vote |
| Sponsorware, early access, sponsor-only repos | Caleb | Effective revenue, but gates code. GitHub supports a private repo per tier ([docs](https://docs.github.com/en/sponsors/receiving-sponsorships-through-github-sponsors/managing-your-sponsorship-tiers)) |
| Invoices and procurement paperwork | QuestPDF Enterprise, Hangfire Enterprise, OSC | Low per deal if a merchant of record or fiscal host does it |

Promises tied to burnout or backlash:

- Open-ended support at low tiers, such as ReactiveUI's 1:1 calls at $50.
- Chat communities. Caleb says to avoid Discord and to turn off GitHub issues in favor of Discussions ([calebporzio.com](https://calebporzio.com/i-just-cracked-1-million-on-github-sponsors-heres-my-playbook)).
- Feature-scoped funding that adds maintenance without paying for it ([Valsorda](https://words.filippo.io/full-time-maintainer/)).
- Salaries sized to donation peaks. Babel cut part-time pay to $2,000/mo and then ran down its balance ([Babel 2021](https://babeljs.io/blog/2021/05/10/funding-update)).
- Any enforcement mechanism inside the package (Moq).

## 4. Platforms

| | GitHub Sponsors | Open Collective (Open Source Collective host) | Polar | thanks.dev |
|---|---|---|---|---|
| Fees | 0% from personal accounts. Up to 6% from organizations (3% card plus 3% service). Invoiced organizations skip the 3% card fee ([docs](https://docs.github.com/en/sponsors/sponsoring-open-source-contributors/about-sponsorships-fees-and-taxes)) | 10% host fee plus processor fees. No setup fee or minimum ([OSC fees](https://docs.oscollective.org/welcome-and-introduction-to-osc/fees)) | Free plan 5% + 50¢. Paid plans 3.4% to 3.8% + 30 to 40¢. +1.5% for non-US cards ([Polar](https://polar.sh/docs/merchant-of-record/fees)) | Not verified. Official docs were unreachable |
| Invoicing for companies | The **sponsor** organization switches to invoiced billing. Minimum $5,000 per invoice, 30-day terms, optional PO number, Stripe-issued invoice, prepaid balance drawn down. Switching cancels existing sponsorships ([docs](https://docs.github.com/en/sponsors/sponsoring-open-source-contributors/paying-for-github-sponsors-by-invoice)) | OSC issues invoices (30-day terms), accepts POs, registers as a vendor in procurement systems, and is already a vendor at Microsoft, Google, Amazon and Meta ([OSC](https://docs.oscollective.org/for-donors-companies-organizations-and-individuals/supporting-projects)) | Merchant of record, so it issues the sale. Its tax handling is not spelled out on the fees page | Organizations pay once, and the platform spreads the money across dependencies. Open Collective ledger entries show payouts from $12 to $6,324 (*secondary*: [OC ledger](https://opencollective.com/thanks-dev/transactions)) |
| Custom amounts and one-time | Yes, with up to 10 one-time tiers | Yes | Products and subscriptions | n/a |
| Sponsor-only repos | Yes, per tier, for an organization-owned private repo | No | Yes (benefits engine) | No |
| Transparency | Private by default | Fully public ledger | Private | Public payouts |
| Who owns the money | The maintainer | The collective. The maintainer is paid through expense claims | The maintainer (it is a sale) | Routed to the maintainer's linked platform |

Other facts:

- Sponsorships are generally not tax-deductible, and telling sponsors so is the recipient's job ([GitHub docs](https://docs.github.com/en/sponsors/sponsoring-open-source-contributors/about-sponsorships-fees-and-taxes)).
- Fiscal hosts can disappear. Open Collective Foundation (a different host from Open Source Collective) announced its dissolution in February 2024 and froze its collectives after September 30, 2024 ([Open Collective statement](https://blog.opencollective.com/open-collective-official-statement-ocf-dissolution/)).
- **NuGet.org has had a "Sponsor" button on package pages since October 2025.** Owners add up to 10 links per package ID (GitHub Sponsors, Open Collective, Patreon, Ko-fi, Tidelift, Liberapay), and the links apply to all versions. NuGet keeps no click analytics ([Microsoft Learn](https://learn.microsoft.com/en-us/nuget/nuget-org/package-sponsorship-on-nuget-org)).
- Corporate benchmark: the Open Source Pledge asks member companies for at least $2,000 per year per employed developer, paid to maintainers ([opensourcepledge.com](https://opensourcepledge.com/)).

**How projects reduce procurement friction:**

1. Let the company's GitHub organization pay by invoice. This only works for sponsors spending $5k+ who already use GitHub billing.
2. Use a fiscal host that is already a registered vendor (OSC).
3. Sell a "license" or "support agreement" through a merchant of record (Paddle for QuestPDF and Lucky Penny, Polar), with reseller listings for enterprise buyers (QuestPDF).
4. Offer one-time 12-month tiers (Homebrew), though Homebrew's have no takers so far.

## 5. Donations compared with commercial revenue

| Project | Donations | Commercial | Source |
|---|---|---|---|
| Babel | Peaked about $150k/yr, about $12k/yr in 2026 | none | [Babel 8](https://babeljs.io/blog/2026/06/16/8.0.0) |
| webpack | about $171k/yr budget | none | [OC](https://opencollective.com/webpack) |
| curl | about $91k/yr, spent on project costs | Maintainer salary from wolfSSL support contracts | [OC](https://opencollective.com/curl), [curl.se](https://curl.se/sponsors.html) |
| Caleb Porzio | about $10k of $1M lifetime from tips | $725k screencasts, $200k logo placement | [calebporzio.com](https://calebporzio.com/i-just-cracked-1-million-on-github-sponsors-heres-my-playbook) |
| Avalonia | about $1.4k/yr on OC, 26 GH sponsors | Per-seat tooling, €21k+/yr enterprise | [OC](https://opencollective.com/avalonia), [pricing](https://avaloniaui.net/pricing) |
| QuestPDF | 4 GH sponsors | $1,999 / $4,999 licenses | [GH](https://github.com/sponsors/QuestPDF), [pricing](https://questpdf.com/pricing.html) |
| Tailwind | Not public | One-time UI-kit sales; reported about 80% revenue drop by 2026 (*secondary*) | [leanware](https://leanware.co/insights/tailwind-ai-crisis) |
| Valsorda | none | Five-figure yearly retainers, about Google-level total | [words.filippo.io](https://words.filippo.io/full-time-maintainer/) |

Across every case with published numbers, **donations alone have not sustained even one full-time maintainer for long**. The largest pure-donation budgets (Babel, webpack, Astro at $430k/yr) are team budgets spread thin, and Babel's collapsed. The money that does sustain people comes from one of three sources:

- Selling something: licenses, tooling, courses, SaaS.
- Selling bounded support or retainers to companies through a contract and an invoice.
- An employer or grant paying for the time: wolfSSL, the Sovereign Tech Agency, Igalia.

In .NET specifically, every project with visible revenue either restricts the license (QuestPDF, Six Labors, Lucky Penny, Xceed, Duende) or sells closed add-ons around an open core (Avalonia, Uno, Hangfire). ADR 0008 rules out the first. It does not address the second.

## 6. Recommendation for Broadside

*This section is recommendation, not sourced finding. It works within ADR 0008 (MIT; sponsorship buys priority, never access) and keeps the three tier families already named in SPONSORS.md.*

### Platform

1. **GitHub Sponsors as the only checkout to start.** It charges 0% on personal sponsors, the account already exists, FUNDING.yml already points to it, and GitHub-billed companies can pay by invoice from $5k.
2. **Add the Sponsor link to every Broadside package ID on NuGet.org**, which is where .NET buyers actually look.
3. **For procurement-bound companies, offer a direct annual Support Agreement** (contract plus invoice, Valsorda-style), sold through a merchant of record (Paddle or Polar) or the owner's own business entity. Add a reseller listing later if enterprise demand appears. QuestPDF shows buyers pay $2k to $5k when given an invoice and paperwork.
4. **Skip Open Collective for now.** The 10% fee and collective-owned funds suit multi-maintainer teams, not a single owner. Revisit if a company insists on a registered vendor, since OSC is already set up at Microsoft, Google, Amazon and Meta.

### Tiers (GitHub Sponsors, monthly)

| Tier | $/month | Promise |
|---|---|---|
| **Individual** | 5 (custom amounts from $5) | Name in SPONSORS.md (opt-in) and the GitHub badge |
| **Individual, generous** | 25 | Same, listed above $5 names. Caleb's $29 tier shows people pay more for nothing extra |
| **Company** | 250 | Small logo in SPONSORS.md and the README sponsor block. Issues from the sponsoring organization get a `sponsor` label and are triaged ahead of the queue (an order, not a time guarantee). One vote in a quarterly roadmap poll, which is advisory only |
| **Company, Gold** | 1,000 | Medium logo on the README and docs-site front page. Everything in Company. Feature requests reviewed at each phase planning, with a written yes or no and the reason |
| **Enterprise** | 3,000 | Large logo. Private support through a sponsor-only GitHub repo (GitHub's per-tier private-repo feature), up to 4 hours of maintainer time per month, async, with a target first response of 2 business days. One 30-minute call per quarter. Bugs they report go first in the queue |

One-time tiers, for companies that cannot hold a subscription (Homebrew's pattern; on-platform ones stay at or under GitHub's $12,000 tier maximum):

- **Company, 12 months: $3,000.** Same promises as Company for 12 months.
- **Company Gold, 12 months: $12,000.** Same promises as Company, Gold.
- **Enterprise, 12 months ($36,000):** off-platform Support Agreement with an invoice, because it exceeds a single tier. Offer the same terms with a PO and net-30.

Pricing rationale: the Company tier ($3k/yr) sits near QuestPDF Professional ($1,999) and Hangfire Business ($1,500). Enterprise ($36k/yr) sits between Duende Standard ($12.5k) and Advanced ($24.9k) plus Avalonia Enterprise (€21k), with support terms deliberately weaker than theirs (no SLA, no fix commitment). It stays well below the commercial PDF SDKs it replaces, whose prices are in [pdf-viewer-landscape.md](pdf-viewer-landscape.md). Keep 5 published recurring tiers. Most successful pages run 5 to 7, and companies cluster at the cheapest company tier.

Set the GitHub Sponsors goal to a concrete monthly number tied to maintainer time (for example, "fund one day a week"), not a percentage. Babel's 1%-of-goal bar shows a goal can also advertise a shortfall.

### Wording rules for SPONSORS.md

- Every promise names a quantity: hours, quarterly, 2 business days. Never "dedicated support" or "priority support" unqualified.
- Say plainly that the 2-business-day figure is a **target, not an SLA**, and that no tier buys a fix date, a feature or a merge.
- State that contributions are not tax-deductible and that no goods are delivered beyond the listed recognition and support time (curl's and Astro's wording).
- Logo rules up front: image only, no text, no tracking parameters, and the owner may decline a sponsor (Sindre's pattern).

### Leave out

- **Any check, telemetry, nag or license key in the packages** (Moq/SponsorLink, and also the Six Labors and Lucky Penny mechanisms). It violates ADR 0008's spirit and cost Moq users.
- **Sponsorware, early-access builds or sponsor-only features.** They work for Caleb, but ADR 0008 says "never access."
- **Response-time SLAs, fix-by commitments, guaranteed feature delivery** at any tier, until there is a team behind them.
- **Private Discord or Slack, and any calls below Enterprise** (ReactiveUI's $50 calls are the counterexample).
- **Binding roadmap votes.** Keep votes advisory. Phases are ordered by dependency ([docs/plan/README.md](../plan/README.md)), so a vote can reorder within a phase only.
- **Per-feature bounties funded by a single company**, unless the price includes ongoing maintenance (Valsorda's argument).
- **Patreon, PayPal and Ko-fi as additional channels** at launch. Each one is another inbox and another tax record, and GitHub plus the NuGet button already cover individuals. Caleb's "diversify" advice applies to the Support Agreement channel, which is the off-GitHub path anyway.

### Expectations

From the evidence, individual sponsors will be a few dozen at best (Avalonia has 26 and QuestPDF 4 on GitHub). Meaningful revenue will come from fewer than ten companies at the Company tier or above, and from Support Agreements. If that does not cover the work once Broadside is past preview, the MIT-compatible lever the .NET field actually uses is paid add-ons around the open core (Avalonia's and Uno's model), for example a commercial viewer-control suite or hosted services. Pursuing that would need its own ADR. It is noted here, not recommended.

## 7. Company tier pricing benchmark

*Added after the owner settled the tier plan: Individual $5/mo, Individual custom from $6/mo, Company $100/mo (small logo plus issue priority, nothing else), and Enterprise as scoped consulting through the owner's company from $5,000 per engagement. This section asks one question: is $100/mo a fair Company price, measured against what .NET teams already pay for PDF functionality? Every price below was read on 2026-11-24 from the vendor's own page unless flagged* secondary. *Prices are list, in USD unless noted, before tax and before promotions (Syncfusion was running a Black Friday code that day; the regular price is used).*

### 7.1 Method

- **Scenario.** A small company with one app on one internal server, licensing a PDF library for 1 developer and for 5 developers.
- **Normalization.** "Year-one $/mo" is the year-one outlay divided by 12. For annual subscriptions, that is also the steady-state cost. For perpetual licenses, later years cost only the optional maintenance renewal (shown where published), so year one overstates their long-run cost; the renewal column gives the steady-state figure.
- **Cheapest fitting SKU.** For each vendor, the cheapest license that legally covers the scenario. Where a 5-developer team fits a site or team SKU that is cheaper than five seats, that SKU is used.

### 7.2 Commercial .NET PDF SDKs

| Vendor / product | SKU used (1 dev / 5 devs) | Unit and term | Year-one $/mo, 1 dev | Year-one $/mo, 5 devs | Renewal | Source |
|---|---|---|---|---|---|---|
| **IronPDF** | Lite $999 / Professional $2,999 | Perpetual. Lite: 1 dev, 1 location, 1 project. Professional: 10 devs, 10 locations, 10 projects | 83 | 250 | Annual support renewal, price not published | [ironpdf.com/licensing](https://ironpdf.com/licensing/) |
| **Aspose.PDF for .NET** | Developer Small Business $1,199 / Site Small Business $5,995 | Perpetual. 1 dev and 1 location; site is up to 10 devs and 10 locations. OEM (public or SaaS distribution) is $3,597 per dev or $16,786 per site | 100 | 500 | Paid support from $399/yr (dev) or $3,995/yr (site) | [purchase.aspose.com](https://purchase.aspose.com/pricing/pdf/net/) |
| **Syncfusion Document SDK** (includes the PDF library) | $1,199 × 1 / × 5 | Per developer per year, subscription. Monthly billing is the annual total split. Free Community License if under $1M revenue, 5 or fewer developers, 10 or fewer employees, and never more than $3M outside capital | 100 | 500 | Same annual price | [syncfusion.com/sales/teamlicense](https://www.syncfusion.com/sales/teamlicense), [community license](https://www.syncfusion.com/sales/communitylicense) |
| **MESCIUS Document Solutions for PDF** (formerly GcPdf) | Developer $999 × 1 / × 5 | Per developer, 1 distribution location, includes 1 year of maintenance. Team Unlimited (5 devs, unlimited locations) is $5,799. No perpetual option mentioned | 83 | 416 | $649/dev/yr; Team Unlimited $3,799/yr | [developer.mescius.com](https://developer.mescius.com/document-solutions/dot-net-pdf-api/pricing) |
| **Telerik DevCraft UI** (cheapest bundle with Document Processing / RadPdfProcessing) | $1,149 × 1 / × 5 | Per developer per year, annual subscription. Document Processing is not sold on its own on this page | 96 | 479 | Renewal page not read | [telerik.com/purchase.aspx](https://www.telerik.com/purchase.aspx) |
| **DevExpress Office & PDF File API** | $1,199.99 × 1 / × 5 less 5% | 12-month subscription. Each developer needs a license; 5% off for 2 to 5 licenses | 100 | 475 | $599.99/yr on-time | [devexpress.com/buy/net](https://www.devexpress.com/buy/net/) |
| **ComponentOne Studio Enterprise** | $1,299 × 1 / × 5 | Per developer per year, subscription. The pricing page does not list a PDF component, so whether C1Pdf is included is unverified | 108 | 541 | Not published | [developer.mescius.com/componentone/pricing](https://developer.mescius.com/componentone/pricing) |
| **Docotic.Pdf** (Bit Miracle) | Server license $995 (both) | Perpetual, one production server; Application license $995 per app; Ultimate $3,495 for any number of servers and apps. Developer count is not the licensing unit | 83 | 83 | Ultimate renewal $1,748/yr | [bitmiracle.com licenses](https://bitmiracle.com/pdf-library/licenses), [buy](https://bitmiracle.com/pdf-library/licenses/buy) |
| **Spire.PDF for .NET** (e-iceblue) | Developer Small Business $999 / Site Small Business $4,599 | Perpetual, internal use only. Developer: 1 dev, 1 location. Site: up to 10 devs, 10 locations. Public, Docker or SaaS use needs OEM ($2,549 dev, $13,088 site) | 83 | 383 | Paid support $499/yr (dev), $2,999/yr (site) | [e-iceblue.com](https://cdn.e-iceblue.com/Buy/Spire.PDF.html) |
| **ABCpdf 14** (WebSupergoo) | Professional $495 per computer; five-pack $1,485 | Per computer. Standard ($329) is 32-bit only. Redistribution or Enterprise $4,950. The licensing page says most buyers "will require an Enterprise License." Assumes dev machines plus one server each need a license (not stated on the price page) | 83 (2 × $495) | 165 (pack + 1) | Upgrade pricing, not a renewal | [websupergoo.com/purchase_abcpdf.htm](https://www.websupergoo.com/purchase_abcpdf.htm), [license](https://www.websupergoo.com/license.htm) |
| **EVO PDF Toolkit** | Deployment $650 (both) | Perpetual, one app on one server, any developers. Company license $1,400 (unlimited devs, apps, servers, redistribution) | 54 | 54 | From $250/yr | [evopdf.com/buy.aspx](https://www.evopdf.com/buy.aspx) |
| **Winnovative PDF Toolkit PRO** | Deployment $650 (both) | Perpetual, one app on one server. Redistributable $1,400 | 54 | 54 | Renewal required for updates, price not on page | [winnovative-software.com/buy.aspx](https://www.winnovative-software.com/buy.aspx) |
| **SelectPdf** | Single Developer $499 / 5-Developers $799 | Perpetual. 1 dev on 1 machine; 5 devs on up to 10 machines. Free Community Edition limited to 5 pages per PDF | 42 | 67 | Optional maintenance, price not on page | [selectpdf.com/pricing](https://selectpdf.com/pricing/) |
| **Foxit PDF SDK** | Quote only | The developer site says licensing is tailored and asks for a quote. *Secondary:* "starting at $3,000 per platform, per year" ([TrustRadius](https://www.trustradius.com/products/foxit-pdf-sdk/pricing)) | ~250 (*secondary*) | ~250 (*secondary*) | n/a | [developers.foxit.com](https://developers.foxit.com/?p=1210) |
| **Apryse (PDFTron) Server SDK** | Quote only | "Custom licensing only", priced by features, document volume and deployment. The page's only figure is that "entry-level packages" start from $1,500, not tied to the .NET SDK | n/a | n/a | n/a | [apryse.com/pricing](https://apryse.com/pricing) |
| **iText Core (.NET)** | Quote only | AGPLv3 for free with full source disclosure of the application; otherwise an OEM license (custom) or an annual subscription priced by the number of PDFs processed, both quote-only | n/a | n/a | n/a | [itextpdf.com/how-buy](https://itextpdf.com/how-buy) |
| **Gnostice Document Studio .NET** | Not shown | Developer subscriptions for 1, 5, 10 or 25 developers in Professional and Ultimate editions, but the price cells were empty on the buy page. PDFOne .NET is not listed | n/a | n/a | 60% renewal discount if renewed within a year | [gnostice.com/buy_now.asp](https://www.gnostice.com/buy_now.asp) |

### 7.3 Open-source and dual-licensed .NET PDF libraries

| Project | License | What a company pays | Year-one $/mo (1 or 5 devs) | Source |
|---|---|---|---|---|
| **QuestPDF** | Community License (source-available), free under $1M annual gross revenue; public-sector and listed companies excluded | Professional $1,999 (one legal entity, unlimited devs, email support) or Enterprise $4,999 (affiliates, priority support, invoicing). Perpetual, with annual renewal of updates at a locked price | 167 (Professional) | [questpdf.com/pricing](https://questpdf.com/pricing.html) |
| **PDFsharp / MigraDoc** | **No longer MIT.** Versions 6.5 and 7.0 are AGPL with an optional commercial license | Commercial €750 one-time, perpetual, unlimited devs and deployments, B2B only. Support: Basic €750/yr (1 to 2 devs, 5 incidents), Standard €1,390/yr (up to 10 devs, 10 incidents); introductory prices €590 and €1,100 | ~€63 | [pdfsharp.com/Pricing](https://www.pdfsharp.com/Pricing), [Offers](https://www.pdfsharp.com/Offers), [LICENSE](https://github.com/empira/PDFsharp/blob/master/LICENSE) |
| **PdfPig** | Apache-2.0 | Nothing. No GitHub Sponsors page exists for the UglyToad account | 0 | [github.com/UglyToad/PdfPig](https://github.com/UglyToad/PdfPig) (Apache-2.0 per GitHub's license metadata), [github.com/sponsors/UglyToad](https://github.com/sponsors/UglyToad) |

The PDFsharp change matters for Broadside's positioning: a widely used managed PDF writer that companies treated as free now needs a €750 commercial license for closed-source use.

Two adjacent .NET reference points (not PDF): **AutoMapper/MediatR** Standard (1 to 10 developers) is listed at $499 or $799 per year ($50 or $80 per month), Professional (11 to 50) at $1,499 or $2,399, Enterprise at $3,999 or $6,399; the page shows two prices per tier without saying what separates them, and the community edition is free under $5M revenue ([automapper.io](https://automapper.io/), [jimmybogard.com](https://www.jimmybogard.com/automapper-and-mediatr-commercial-editions-launch-today/)). **Hangfire** Pro Startup is $500 per organization per year (section 1.2).

### 7.4 What the numbers say

- **One mainstream developer seat costs about $1,000 to $1,300 a year.** Aspose, Syncfusion, DevExpress, MESCIUS, Telerik and ComponentOne all land at $83 to $108 a month for one developer in year one. Across the 15 priced rows in 7.2 and 7.3 (excluding Foxit's secondary figure), the median is **$83/mo for 1 developer** and **$250/mo for 5 developers**.
- **The cheap end is per-deployment, not per-seat.** EVO, Winnovative, Docotic and SelectPdf sell one server or one app for $499 to $995 perpetual, which is $42 to $83/mo in year one and close to nothing after. These are mostly HTML-to-PDF converters or narrower toolkits, not a full read/write/edit/render stack.
- **The expensive end hides its prices.** iText, Apryse and Foxit (the three with full rendering and editing engines closest to Broadside's scope) are quote-only. The only figures are Apryse's "from $1,500" entry package and a secondary $3,000/platform/year for Foxit.
- **The closest peer charges a company $1,999 once.** QuestPDF Professional is about $167/mo in year one for unlimited developers, and it includes direct email support plus the legal right to use the library. Broadside's Company tier gives neither (the library is MIT regardless), only a logo and triage order.
- **$100/mo is exactly one developer seat** of Aspose, Syncfusion or DevExpress, and **20% to 25% of what a 5-developer team pays them**.

### 7.5 Company sponsorship and what maintainers say about pricing it

| Source | Entry company price | What it buys | Source |
|---|---|---|---|
| Vue Bronze, Babel Bronze, curl Silver, Astro Sponsor | $100/mo | Small logo or listing | Section 1.1 |
| Homebrew Sponsor, Caleb Porzio business | $250/mo | Name in README; nothing listed | Section 1.1 |
| QuestPDF GitHub Sponsors | $100/mo for "Prioritized support" | 4 current sponsors in total | Section 1.2 |
| Open Source Pledge | more than $2,000 per employed developer per year, spread across all OSS a company uses | Membership listing | [opensourcepledge.com](https://opensourcepledge.com/) |
| Sentry (pledge founder), 2024 | $750,000 across hundreds of projects, which the post says is $5,813 per engineer for 129 engineers | Funding, no deliverables | [blog.sentry.io, 2024-11-12](https://blog.sentry.io/we-just-gave-750-000-dollars-to-open-source-maintainers) |
| wolfSSL (employer of curl's maintainer) support | Basic $2,600/yr (3 incidents, 3 business days); Standard $8,500/yr | Email support with incident caps | [wolfssl.com](https://www.wolfssl.com/products/support-and-maintenance/) |
| Geomys / Filippo Valsorda retainers | "five-figures range (USD, yearly)"; no public price on the Geomys site | Access and advice, not deliverables | [words.filippo.io](https://words.filippo.io/full-time-maintainer/), [geomys.org](https://geomys.org/) |

Primary arguments that companies under-pay:

- Valsorda compares the price to the alternative, not to a donation: he writes that "dedicating an engineer to that task would probably be more expensive than my rate," and calls his tiers "significantly cheaper than an employee" ([words.filippo.io](https://words.filippo.io/full-time-maintainer/)).
- Armin Ronacher (Sentry) backs the $2,000-per-engineer pledge as modest, because it is less than a fifth of the US payroll tax on a typical developer, and notes that companies "are unlikely to fund something without understanding the return on investment" ([lucumr.pocoo.org, 2024-09-19](https://lucumr.pocoo.org/2024/9/19/open-source-tax/)).
- Section 1 already records the outcome of under-pricing: QuestPDF's $100 "prioritized support" sponsorship has 4 sponsors, while its $1,999 license sells. Caleb Porzio's $1M came from selling things, with $200k from companies buying logo placement ([calebporzio.com](https://calebporzio.com/i-just-cracked-1-million-on-github-sponsors-heres-my-playbook)).

No primary source located the threshold below which an engineering manager can expense a purchase without procurement; these limits are company-specific, and nothing citable was found. The one hard platform line is GitHub's: organizations can switch to invoiced Sponsors billing only at $5,000 or more per invoice (section 4), and a single tier tops out at $12,000/month. A Company tier under about $415/mo ($5,000/yr) will therefore be paid by card, through GitHub's normal checkout, at up to 6% in fees for organization sponsors (section 4). No source was found that states what fraction of a commercial seat makes sponsorship an easy yes; the reasoning below on that point is judgment, not evidence.

### 7.6 Recommendation: Company tier

*Recommendation, not sourced finding. It keeps the owner's constraints: MIT forever, no gated features, Company buys a small logo and issue priority only, Enterprise is separate consulting from $5,000.*

**Set the Company tier at $250/month** (with a one-time 12-month option at $3,000 for companies that cannot hold a subscription). Do not add a second company tier.

Reasoning:

1. **It prices against the displaced cost, not against a tip jar.** A 5-developer .NET team buying a mainstream PDF SDK pays $380 to $540 a month (Spire, MESCIUS, DevExpress, Telerik, Aspose, Syncfusion, ComponentOne), and the median of all priced rows is $250. $250 is about half of what the mainstream suites charge that team and roughly equal to the cheapest full per-seat option. That is a price an engineering manager can defend in one sentence: "we pay half of what Aspose would cost and get a library we can fork."
2. **It stays honest against QuestPDF.** QuestPDF Professional is $1,999 once (about $167/mo in year one), with a license and email support. $250/mo ($3,000/yr) is above that, but QuestPDF's renewal-only years are cheaper, and Broadside's promise is weaker. The gap is justified only because Broadside covers rendering and editing, which QuestPDF does not; the tier copy should say what it funds, not imply it equals a support contract.
3. **It sits in the observed company-entry band.** Companies cluster at the cheapest company tier (section 2), and $100 to $250 is where that tier sits across Vue, Babel, curl, Homebrew and Caleb Porzio. $250 is the top of that band, which suits a library that replaces a paid product rather than a convenience dependency.
4. **It stays a card purchase.** $3,000 a year is under GitHub's $5,000 invoicing floor, so it goes through the normal Sponsors checkout. Anything that needs a PO or invoice is already the Enterprise consulting path.
5. **No second tier.** The evidence does not support one: tiers above $1,000 hold a handful of sponsors or none (section 2), and a higher tier would need a promise beyond logo and priority, which the owner has ruled out. Companies that want more already have Enterprise consulting.

**Is $100/month too low?** Yes, for any company with more than one or two developers. $100 is one developer seat of Aspose, Syncfusion or DevExpress, and 20% to 25% of what a 5-developer team pays them. It is not unreasonable for a one-person or two-person company, and those buyers are already covered: the Individual custom tier (from $6) lets a small company give $100 without a logo. Because the cheapest company tier is where companies cluster, its price effectively sets Broadside's company revenue, so under-pricing it costs more than any other tier decision.

If the owner wants a softer step, $200/mo ($2,400/yr) is defensible (two mainstream seats, slightly above QuestPDF Professional's year-one cost). Below $167/mo the tier asks less than the closest peer charges for one company and one perpetual license, which is the under-charging the owner set out to avoid.

## 8. Decisions

*Decided by the owner on 2026-11-24, after sections 1 to 7. Where this differs from sections 6 and 7.6, this section wins.*

**Purpose.** Sponsorship covers the project's running costs, chiefly AI tooling, while the owner works on Broadside part time. Full-time funding is not a goal. The public GitHub Sponsors goal is **$200 a month**: in the 30 days to 2026-11-24 Broadside used about a third of the owner's Claude plan, GitHub Actions cost nothing (public repository, standard runners), and the rest is headroom.

| Tier | Price | Recognition | Priority |
|---|---|---|---|
| Individual | $5 a month | Name in SPONSORS.md | 4 |
| Individual, your amount | Custom amount, minimum $6 a month | Name in SPONSORS.md | 3 |
| Company | $150 a month | Small logo in README and docs site | 2 |
| Enterprise | Consulting through TheEightBot, scoped and invoiced per engagement, minimum $5,000 | Large logo, with permission, as "Enterprise partner, via TheEightBot" | 1 |

- **Promises are recognition and queue order only.** No vote, call, chat channel, support channel, response time or fix-by date at any tier. This is narrower than section 6, which offered a roadmap vote, written feature decisions and capped Enterprise support hours.
- **Company is $150, not $250 (7.6) or $100 (the owner's first proposal).** It sits between one mainstream SDK seat (about $100 a month) and the 5-developer median (about $250), and above QuestPDF Professional's year-one cost (about $167) only by a little; see 7.1 and 7.6.
- **One company tier.** No Company Gold or higher monthly tier.
- **Priority needs an active monthly sponsorship.** One-time sponsorships get a name listed only. There is no annual one-time Company option (section 6 proposed $3,000 for 12 months): anything beyond the monthly tiers, including annual or invoiced sponsorship, goes to Enterprise at $5,000 or more, which also matches GitHub's $5,000 invoicing minimum (section 4).
- **Enterprise work on Broadside lands in this repository under MIT.** Code specific to a client's application stays the client's. This keeps ADR 0008: money moves work earlier, never makes it exclusive.
- **Platforms.** GitHub Sponsors, a Sponsor link on every Broadside package on NuGet.org, and TheEightBot for Enterprise. No Open Collective, Patreon, PayPal or Ko-fi.
- **Nothing closed or commercial.** Paid add-ons around the open core (section 5) are ruled out, not deferred.
- **Automation.** `.github/workflows/sponsor-labels.yml` labels issues from active monthly sponsors `sponsor:company` or `sponsor:individual`.

