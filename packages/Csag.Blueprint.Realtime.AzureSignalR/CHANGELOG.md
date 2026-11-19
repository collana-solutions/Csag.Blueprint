# @neolution-ch/csag-blueprint-realtime-azure-signalr

## 0.12.0

### Minor Changes

- [#98](https://github.com/collana-solutions/Csag.Blueprint/pull/98) [`2e1e3f5`](https://github.com/collana-solutions/Csag.Blueprint/commit/2e1e3f5ff12491315e84c80e0bc906fbfb49ccb0) Thanks [@neotrow](https://github.com/neotrow)! - Let the Azure SignalR provider authenticate with Microsoft Entra ID instead of an access key
  
  `BlueprintAzureSignalROptions` has two new properties, `Endpoint` and `Credential`. Together they replace `ConnectionString`: the provider then calls the service's REST API with the credential's token and signs client tokens with a temporary key that it fetches from the service with the same token. No access key is needed, so the service can run with local authentication disabled. `Endpoint` binds from configuration. `Credential` is a `TokenCredential` that the host sets in code, for example from `Csag.WorkloadIdentity`.
  
  The credential's identity needs the built-in roles **SignalR REST API Owner** (publishing and group membership) and **SignalR App Server** (the temporary key) on the SignalR resource. The package README describes the setup.
  
  The startup validation now requires exactly one of `ConnectionString`, or `Endpoint` with `Credential`, and an `Endpoint` must be an absolute https URI. A configuration that sets only `ConnectionString` works as before.

## 0.11.0

### Minor Changes

- [#93](https://github.com/neolution-ch/Csag.Blueprint/pull/93) [`44a8582`](https://github.com/neolution-ch/Csag.Blueprint/commit/44a858269c0549e7624f29b568e1e8b4cf068b65) Thanks [@neotrow](https://github.com/neotrow)! - Add the real-time channel: tenant-scoped signals from the API to the browser, with Azure SignalR Service as the first provider
  
  - **Csag.Blueprint.Application** (`Csag.Blueprint.Application.Abstractions.Realtime`): the transport-neutral contracts. `IRealtimeNotifier` publishes to a tenant, one user or several users of a tenant, and has no way to reach every tenant. `IRealtimeTicketIssuer` hands a browser a `RealtimeTicket` (client URL, access token, expiry). `RealtimeEvent` is the envelope: event name, entity id, and the time from an injected `TimeProvider`, nothing else. `NoOpRealtimeService` implements both interfaces for a host without real-time.
  - **Csag.Blueprint.Realtime.AzureSignalR** (new package): `AddBlueprintAzureSignalRRealtime(options => ...)` registers both interfaces on Azure SignalR Service in serverless mode. The API holds no connection: it grants group membership, mints client tokens and publishes over the REST API while it serves a request, so it can scale to zero. `BlueprintAzureSignalROptions` (connection string, hub name, ticket lifetime, group membership TTL) is validated at startup, including the connection string through the SDK's own parser.
  - **Csag.Blueprint.Testing** (`Csag.Blueprint.Testing.Realtime`): `RecordingRealtimeNotifier` keeps every notification in memory, so an integration test can assert what an endpoint published.
