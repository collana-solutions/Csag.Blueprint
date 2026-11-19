---
"@neolution-ch/csag-blueprint-realtime-azure-signalr": minor
---

Let the Azure SignalR provider authenticate with Microsoft Entra ID instead of an access key

`BlueprintAzureSignalROptions` has two new properties, `Endpoint` and `Credential`. Together they replace `ConnectionString`: the provider then calls the service's REST API with the credential's token and signs client tokens with a temporary key that it fetches from the service with the same token. No access key is needed, so the service can run with local authentication disabled. `Endpoint` binds from configuration. `Credential` is a `TokenCredential` that the host sets in code, for example from `Csag.WorkloadIdentity`.

The credential's identity needs the built-in roles **SignalR REST API Owner** (publishing and group membership) and **SignalR App Server** (the temporary key) on the SignalR resource. The package README describes the setup.

The startup validation now requires exactly one of `ConnectionString`, or `Endpoint` with `Credential`, and an `Endpoint` must be an absolute https URI. A configuration that sets only `ConnectionString` works as before.
