# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased

## 1.0.1 - 2026-09-09
- Sentinel connection uses the default 5 s sync/async timeouts instead of 500 ms - a slow read timed
  out and was reported to callers as a missing value (ASP.NET session "not available")

## 1.0.0 - 2026-04-21
- Initial implementation
