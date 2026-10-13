# ADR-0019: Display texts allow letters, digits, spaces, `-` and `_` only

- **Status:** Accepted
- **Context:**
  - The requester asked that name and description texts allow only letters and digits (DRK-2190, round 2 answer 2). In round 3 they added spaces, `-` and `_`.
  - Most texts are keys or ids, not names. Their examples hold `.`, `@` or `-`: setting key `customers.list.table`, purpose `customer-onboarding`, file name `passport.pdf`, and owners, often user ids with `-` or `@`.
  - The requester agreed the rule covers only the 3 display texts (round 3 answer 1).
- **Decision:**
  - 3 display texts allow only the letters a-z and A-Z, the digits 0-9, spaces, `-` and `_`: the file group name, the setting group name and the setting group description.
  - Allowed: `Onboarding pack 2026`, `Pack-2`, `my_layouts`. Refused with 400: `Café`, `Pack, v2.`, `Pack/2`.
  - Every other text keeps the rule it had: no control characters.
  - The trim (ADR-0018) runs first, so outer spaces never fail the rule.
- **Alternatives:**
  - *Letters and digits only.* Rejected by the requester: names would lose their spaces.
  - *The same rule on every text.* Rejected: it would refuse keys, file names and owners in their documented shapes.
  - *No character rule, as before.* Rejected by the requester.
- **Consequences:**
  - Easier: display texts are safe to show anywhere without escaping.
  - Harder: no accents, commas or full stops in names and descriptions. The merged file group routes refuse names they accepted before. Nothing is released yet, so no caller breaks.
