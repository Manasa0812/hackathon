# ResolveIQ investigator in Switch

ResolveIQ's Switch agent investigates incidents end to end. It should choose an investigation path based on the evidence rather than follow a fixed endpoint checklist. Correlate the label and configuration services, PostgreSQL, Redis, Prometheus/Grafana metrics, and Loki logs, then report findings in the Mattermost room.

## What a reviewer needs

The API and observability demo runs from this repository with Docker Compose. To reproduce the chat interaction, a reviewer also needs Switch Console, a Codex provider they can use, and the .NET 10 SDK to run the MCP server. Switch Console runs a local Switch server and includes a local Mattermost chat; they do not need to install Mattermost separately or access the submitter's private local room. They configure their own Switch agent and room, then add this project's MCP server to the Codex provider on the machine where Switch runs. Switch is free to self-host; model access may require the reviewer's own Codex subscription or API credentials.

This is why the submission video matters: it shows the real chat workflow on the submitter's running Switch setup, while the public repository lets reviewers reproduce the services and, if they choose, connect their own Switch instance.

## Start the local dependencies

Run the ResolveIQ stack from the project directory:

```bash
docker compose up --build
```

The app, databases, telemetry tools, and agent run on the same development laptop. Application and observability ports are bound to loopback.

For the full chat flow, open Switch Console, start its local server, create or open a Mattermost room, and add a Codex-backed agent named `resolveiq-investigator`. The room is local to that Switch installation; it is not a hosted shared room for judges.

## Connect the MCP tools to Codex

Switch starts the chosen provider on the agent's run machine. Add this server to Codex's MCP configuration on that machine, replacing `<absolute-path-to-cloned-ResolveIQ>` with the actual path to the cloned repository. Keep database credentials out of this file; the MCP server uses the local observer-role defaults unless Compose `.env` overrides them.

```toml
[mcp_servers.resolveiq_investigator]
command = "dotnet"
args = ["run", "--project", "<absolute-path-to-cloned-ResolveIQ>/src/ResolveIQ.InvestigatorMcp/ResolveIQ.InvestigatorMcp.csproj"]
startup_timeout_sec = 30
```

The server reads local defaults for ConfigService, LabelService, PostgreSQL, Redis, Prometheus, and Loki. If `RESOLVEIQ_READONLY_PASSWORD` is overridden in `.env`, set `ConnectionStrings__ReadOnlyDatabase` in the MCP server environment to the matching read-only connection string. Never use the application database owner's credentials for the agent.

Use the reusable prompt in [`resolveiq-investigator-prompt.md`](resolveiq-investigator-prompt.md) as the Switch agent's instructions. After changing Codex's MCP configuration, start a new investigator session in Switch and confirm the ResolveIQ tools appear. MCP server configuration is part of the Codex provider setup, separate from the Switch agent's instructions.

## Demo conversation

1. Generate a failed label using an unconfigured destination postcode or an oversized parcel using the root README examples.
2. In the Switch room, mention `@resolveiq-investigator` and ask it to investigate `ZONE_NOT_FOUND` or `RATE_NOT_FOUND` over a time window.
3. Ask it to count the errors in Prometheus and correlate ConfigService and LabelService logs, including trace IDs.
4. Open Grafana at `http://localhost:3000` to see the same request and dependency signals.

For an on-demand investigation, ask about a known error code or HTTP status and a time window, for example: “Investigate ZONE_NOT_FOUND over the last 5 minutes. Count matching errors in Prometheus, search both services' Loki logs, correlate the evidence, and explain the likely cause.” The `InvestigateObservedErrors` MCP tool queries the bounded error-code/status allowlist and searches logs across LabelService and ConfigService. `GetCarrierQuote` can make a read-only zone-then-rate lookup when shipment details are supplied.

The MCP server exposes read-only tools. It does not execute arbitrary SQL, return Redis key values, create labels or alert events, restart containers, or change configuration.
