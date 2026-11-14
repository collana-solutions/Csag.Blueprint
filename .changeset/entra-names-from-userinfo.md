---
"@neolution-ch/csag-blueprint-web": patch
---

Give new Entra accounts the first name and the last name that Entra sends through UserInfo

Without optional claims in the app registration, Entra does not put `given_name` and `family_name` in the ID token. It sends them only through its UserInfo endpoint. The Entra profile did not map these values to `ClaimTypes.GivenName` and `ClaimTypes.Surname`, so the external callback created the account with an empty first name and an empty last name. The Entra profile now maps them. When the ID token and UserInfo both have a name, the name from the ID token stays.

The Entra profile also maps the UserInfo `email` to `ClaimTypes.Email`, but only when the sign-in has no email yet and the email trust that the package computes from the ID token is true. An untrusted multi-tenant sign-in still gets no email, and the address that links accounts does not change.

`GetClaimsFromUserInfoEndpoint` (default: true) must stay on for an Entra provider, unless the app registration adds the names as optional claims. Accounts that earlier sign-ins created without names keep their empty names.
