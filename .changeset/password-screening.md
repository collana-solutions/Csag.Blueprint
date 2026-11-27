---
"@collana-solutions/csag-blueprint-web": minor
---

Screen passwords against common passwords, context-specific words, the account's own names and breach data

- `WithStrongPasswordRules` refuses a password on the embedded common-password list (`CommonPasswordList`, 40,000 passwords of 8 or more characters, at least 10,000 of them for any minimum length up to 15) and a password that contains a word of the new `PasswordSettings.ContextSpecificWords`.
- `AddBlueprintPasswordScreening()` on the Identity builder adds `BlueprintPasswordValidator<TUser>`, which screens every password `UserManager` sets: the common-password list, `ContextSpecificWords`, the account's email local part and user name, and an optional breached-password check.
- `PasswordSettings.BreachCheck` configures that check: `Mode` (`Disabled` by default, `Online` for the Pwned Passwords range API with k-anonymity and padding, `Offline` for a local directory of range files), `FailOpen` (default `true`), `TimeoutMilliseconds` (default 2000), `RangeApiBaseUrl` and `OfflineRangeDirectory`. Startup validation checks the words and these settings.

**Behaviour change:** `WithStrongPasswordRules` now refuses common passwords even if the application changes nothing else. A test, seed or fixture that sets a password such as `Password1234` through a validator that uses it now gets a validation error.

**To upgrade:**

1. Call `.AddBlueprintPasswordScreening()` on the application's Identity builder, after `AddIdentity` or `AddIdentityCore`.
2. List the organisation, product and application names under `Blueprint:Security:PasswordSettings:ContextSpecificWords`. Each word needs at least 3 letters or digits.
3. Add the translation keys `Validation.PasswordTooCommon`, `Validation.PasswordContainsContextWord` (with a `{Word}` placeholder), `Validation.PasswordContainsAccountName`, `Validation.PasswordBreached` and `Validation.PasswordBreachCheckUnavailable`. Until they exist, the English fallback is shown.
4. To check breach data, set `Blueprint:Security:PasswordSettings:BreachCheck:Mode` to `Online` and allow outbound HTTPS to `api.pwnedpasswords.com`, or to `Offline` with `OfflineRangeDirectory`.
5. With step 1, a test that creates a user through `UserManager` with a password containing its own email local part, such as `Test@123` for `…_test@test.local`, is refused. Give such fixtures a password unrelated to the address.
