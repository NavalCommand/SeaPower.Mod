# Primary platform role review

Reviewed against the installed original Sea Power 8.3 content on 2026-10-16.
`platform-primary-roles.csv` is the maintained per-type decision ledger. The
runtime consumes generated `platform-roles.ini`, not this ledger or the internet.
The generator requires an explicit row for every inventory entry and rejects
new/unreviewed or stale entries rather than manufacturing a primary role.

## Interpretation

A primary describes an intrinsic specialization, independent of loadout, scenario
orders and capability-list order. Secondary capabilities alone do not make a
specialist multipurpose. Udaloy and Spruance therefore retain ASW. General-purpose
patrol/attack submarines, fleet carriers and combined fighter/attack platforms can
have capabilities without any primary. Empty primary INI entries are intentional
and clear native defaults. GuidedMissile on explicitly designated missile cruisers
is the owner's requested designation/capability exception, not a single warfighting
mission; Ticonderoga retains CG.

Each row records the decision, its basis and source IDs. Family/type decisions are
**our classification inferences**, not quotations or native primary-role facts.
Sources about a platform family are applied to its in-game variants only when the
variant does not change that specialization. `GAME` identifies the exact local
variant/type/AI roles/weapon target metadata. `AIR-TYPES`, `NWP`, `ADA` and `ARMY`
provide classification vocabulary and mission-family context, not independent
historical verification of every export variant. This is a complete inventory
review with conservative classifications, not a claim that every game's variant
has an independently published primary-mission statement.

Disposition meanings:

- Primary: an intrinsic specialization is selected.
- Designation: guided-missile cruiser capability exception described above.
- Multipurpose: no preference among the platform's supported purposes.
- Unrepresented: a specialized purpose outside the current generic role vocabulary,
  or a supporting ammunition payload that is not an independent platform.
- Unspecified: identity/purpose insufficient, nonmilitary scenery, or an unbuilt/
  fictional game type. Absence here does not assert that a real platform is multirole.

The 558 platform rows include aircraft, vessels, installations, land vehicles and
animals. The 444 ammunition rows include tactical weapons/devices and supporting
payloads (pods, fuel tanks, projectiles); inclusion is not a claim that every payload
becomes a tactical object. AI roles remain capability evidence, not mission priority.
Weapon primary choices follow explicit single target-domain metadata. Dual-domain
torpedoes remain multipurpose. The native ASuW target enum also covers land: weapons
that explicitly exclude vessels and support land attack are classified LandAttack.
No original game files or proprietary extracts are distributed here.

## Sources

Sources were consulted through web search and, for SOVIET, the complete downloaded
publication. Current pages provide mission descriptions, not current fleet totals.
The historical game-era variants and exact IDs come from installed GAME metadata.

| ID | Primary source and scope |
|---|---|
| GAME | Installed `original/{aircraft,vessels,land_units,biologic,ammunition}/*.ini` and `original/language_en/*_names.ini`. Type-specific native purpose and variant metadata; read only. |
| AIR-TYPES | [NATO APP-06(E)(1), tables 1-3, 2-4 and annexes](https://studylib.net/doc/28067209/app-06-ede-v1-e). NATO-authored publication hosted by a third party; role vocabulary and modifier assignments. |
| SOVIET | [Department of the Navy, Understanding Soviet Naval Developments, 6th ed., July 1991](https://www.govinfo.gov/app/details/GOVPUB-D201-PURL-LPS106117). Original PDF pages 139–166 and 177–190: Soviet submarine, combatant and aviation families. Udaloy is explicitly principally ASW (PDF 162); Sovremennyy principally surface warfare (163); Krivak principally ASW (166). Kilo is described as a torpedo attack boat (146); no ASW priority is asserted. Kiev's mixed aviation and weapons support leaving primary unset (154). |
| SUB | [US Navy, Attack Submarines—SSN](https://www.navy.mil/DesktopModules/ArticleCS/Print.aspx?Article=2169558&ModuleId=724&PortalId=1). Multiple attack-submarine missions; general-purpose SSN policy. Applied to non-US torpedo boats as a conservative policy inference, not proof of identical doctrine. |
| KILO | [Rosoboronexport, Project 636 brochure](https://roe.ru/pdfs/pdf_6484.pdf). Related Kilo-family mission context; the exact in-game Project 877 fit is determined from GAME and SOVIET, not imported from a modern 636 upgrade. |
| SSBN | [US Pacific Submarine Force, Submarines](https://www.csp.navy.mil/SUBPAC-Commands/Submarines/). Strategic deterrence specialization. Soviet game-era ballistic boats also checked in SOVIET. |
| CARRIER | [USS Nimitz, About Us](https://www.airpac.navy.mil/Organization/USS-Nimitz-CVN-68/About-Us/), [NAVSEA carrier centennial](https://www.navsea.navy.mil/Media/News/Article/3034264/aircraft-carrier-centennial-uss-nimitz-cvn-68/). Multimission carriers; fleet-carrier policy inference. |
| SPRUANCE | [NHHC, Spruance I (DD-963)](https://www.history.navy.mil/research/histories/ship-histories/danfs/s/spruance.html). Principal ASW design purpose despite other capabilities. |
| ESCORT | [NHHC, Ainsworth (FF-1090)](https://www.history.navy.mil/content/history/nhhc/research/histories/ship-histories/danfs/a/ainsworth.html). Ocean escort ASW employment; combined with GAME's explicit ASW classification. |
| NWP | [US Navy NWP 1 A, Strategic Concepts](https://www.history.navy.mil/research/library/online-reading-room/title-list-alphabetically/s/strategic-concepts-usnavy.html). Generic sea control, strike, logistic and amphibious mission context. No priority inferred for a general escort. |
| MINE | [NHHC, Ability II (MSO-519)](https://www.history.navy.mil/research/histories/ship-histories/danfs/a/ability-mso-519-ii.html). Minesweeping family purpose; exact class/designation from GAME. |
| F4 | [USAF Museum, F-4C](https://www.nationalmuseum.af.mil/Visit/Museum-Exhibits/Fact-Sheets/Display/Article/2929390/mcdonnell-douglas-f-4c-phantom-ii/). Fighter and air-to-ground capabilities. |
| F18 | [NHHC, F/A-18 Hornet](https://www.history.navy.mil/content/history/museums/nnam/explore/collections/aircraft/f/f-a-18-hornet--tactical-.html). Multimission fighter/attack design. |
| F15 | [USAF, F-15 Eagle fact sheet](https://www.af.mil/About-Us/Fact-Sheets/Display/Article/104501/f-15-eagle/source/f-15-eagle/). Air-superiority design for in-game A/C variants. |
| F14 | [NHHC, F-14A Tomcat](https://www.history.navy.mil/content/history/museums/nnam/explore/collections/aircraft/f/f-14a-tomcat.html). Fleet-fighter design. |
| MIRAGE | [Dassault, Mirage III](https://www.dassault-aviation.com/en/passion/aircraft/military-dassault-aircraft/mirage-iii/), [Dassault, Mirage F1](https://www.dassault-aviation.com/fr/defense/soutien-militaire/avions-soutenus/mirage-f1/). Variant distinctions; conservative no-primary for ambiguous/multirole variants. |
| EA6 | [NHHC, EA-6B Prowler](https://www.history.navy.mil/content/history/museums/nnam/explore/collections/aircraft/e/ea-6b-prowler0.html/1000). Electronic attack specialization. |
| E2 | [NAVAIR, E-2C](https://www.navair.navy.mil/product/E-2C-Hawkeye). AEW/battle management. |
| P3 | [NAVAIR, P-3C](https://www.navair.navy.mil/product/P-3C-Orion). ASW patrol specialization with ASuW and surveillance capabilities. |
| S3 | [NHHC, S-3 Viking](https://www.history.navy.mil/content/history/museums/nnam/explore/collections/aircraft/s/s-3b-viking--navy-one-.html). ASW origin; review uses Cold War S-3A, not later changed S-3B employment. |
| H3 | [NHHC, SH-3 Sea King](https://www.history.navy.mil/content/history/museums/nnam/explore/collections/aircraft/s/sh-3a-sea-king.html). ASW procurement purpose, separate rescue variants. |
| DASH | [NHHC, QH-50C DASH](https://www.history.navy.mil/our-collections/photography/numerical-list-of-images/nhhc-series/nh-series/USN-1111000/USN-1111342.html). Antisubmarine drone purpose. |
| PATROL | [ShinMaywa, development history](https://www.shinmaywa.co.jp/english/products/aircraft/amphibian/us2/history04.html). PS-1 ASW versus US-1 rescue distinction. Other patrol types use GAME's exact variant. |
| FOXBAT | [USAF Museum, MiG-25](https://www.nationalmuseum.af.mil/Visit/Museum-Exhibits/Fact-Sheets/Display/Article/196331/AFmuseum/mikoyan-gurevich-mig-25/). Reconnaissance/interceptor variants; GAME identifies RB. |
| ATTACK | [NHHC, AV-8 Harrier](https://www.history.navy.mil/content/history/museums/nnam/explore/collections/aircraft/a/av-8c-harrier0.html), [JASDF, F-1](https://www.mod.go.jp/asdf/airpark/GUIDE/equipment/aircraft/f1.html). Attack/support variants; Yak-38 also checked in SOVIET. |
| ADA | [US Army, Air Defense Artillery career field](https://api.army.mil/e2/c/downloads/2024/08/09/c42480dd/cmf-14-da-pam-600-25-9-aug-24.pdf). Air-defence and sensing mission vocabulary; GAME supplies specific weapon type. |
| ARMY | [US Army, 2001 Weapon Systems handbook](https://asc.army.mil/docs/wsh2/2001-wsh.pdf). Transport, combat and field-artillery purpose families; no 2001 equipment fit copied into Cold War units. |
| WEAPONS | [US Navy procurement highlights](https://www.secnav.navy.mil/fmc/fmb/Documents/22pres/Highlights_Book.pdf). Weapon role vocabulary and dual ASW/ASuW torpedo example; exact target-domain choices come from each GAME definition. |
