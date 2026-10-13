# ADR-0015: The health detail route is anonymous

- **Status:** Accepted
- **Context:**
  - Revision 1 kept the scaffold default: `/healthz` is anonymous, and `/healthz/detail` needs a valid token.
  - The requester decided on 2026-10-09 (DRK-2187) that both health routes are anonymous.
  - The detail report shows each check's name, status, duration and failure message. A database failure message can hold server names or error text.
  - The service is reachable only inside the cluster. The Helm chart's HTTP route is off by default.
- **Decision:**
  - `/healthz` and `/healthz/detail` both allow anonymous calls.
  - `/healthz` still reports status only. `/healthz/detail` gives the per-check report.
- **Alternatives:**
  - *Keep the token on `/healthz/detail`.* Rejected by the requester.
  - *Drop the detail route.* Not chosen: operators read it to find which check fails.
- **Consequences:**
  - Easier: an operator or a probe reads the detail report without a token.
  - Harder: anyone who reaches the service in the cluster can read database failure text. The requester accepted this risk.
  - Harder: a deployment that turns on the chart's HTTP route exposes the detail report outside the cluster.
