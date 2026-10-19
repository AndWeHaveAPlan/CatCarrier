# CatCarrier

Prometheus exporter for GitHub Actions.

It serves `/metrics` and receives `workflow_job` and `workflow_run` webhooks at
`/github`. It can also poll the organisation runners endpoint, which the
webhooks cannot answer.

## Webhook metrics

Counters, labelled by organisation, repo, workflow and job.

| Metric | Meaning |
| --- | --- |
| `github_workflow_job_duration_seconds` | Job seconds, added when a job completes |
| `github_workflow_job_startup_seconds` | Queue seconds, added when a job completes |
| `github_workflow_job_fails_count` | Jobs that completed with conclusion `success` |
| `github_workflow_run_duration_seconds` | Workflow run seconds |
| `catcarrier_exceptions` | Webhook requests that threw or returned a non-200 |

`rate()` over a seconds-counter gives a mean concurrency. `rate()` over
`github_workflow_job_duration_seconds` is the mean number of busy runners, and
`rate()` over `github_workflow_job_startup_seconds` is the mean queue depth.

Every counter moves only when a job completes, so all of them lag by up to one
job length. They cannot say what is happening right now.

## Runner metrics

Gauges from `GET /orgs/{org}/actions/runners`, which reports each runner as
online or offline and busy or idle. Polling is off unless configured, and
answers the question the counters cannot: how many runners are busy at this
moment.

| Metric | Labels | Meaning |
| --- | --- | --- |
| `github_runners_registered` | `organization`, `runner_label` | Runners carrying this label |
| `github_runners_online` | `organization`, `runner_label` | Of those, status online |
| `github_runners_busy` | `organization`, `runner_label` | Of those, reporting busy |
| `github_runners_total` | `organization` | Runners in the org, counted once each |
| `github_runners_last_success_timestamp_seconds` | `organization` | Unix time of the last poll that succeeded |

A runner carries several labels and is counted under each one. Never sum
`github_runners_*` across `runner_label`. Use `github_runners_total` for a
headcount.

A label that disappears from the fleet has its series removed, so a retired
machine class does not sit at its last value for ever.

### Always alert on the heartbeat

```promql
time() - github_runners_last_success_timestamp_seconds > 300
```

The poller retries for ever and never exits, so a permanent breakage looks
exactly like a healthy system from outside. The process keeps serving, `up`
stays 1, and the gauges keep returning whatever was last written.

The gauges cannot reveal this on their own. A correct reading of an idle fleet
is a constant zero, and a frozen reading is a constant too, so `changes()` and
`delta()` cannot separate them. The heartbeat is the only series that answers
"is this number still true".

A rotated key, a revoked App permission, clock skew, an exhausted rate limit and
a blocked egress rule all produce this. Each is logged. Nothing on a dashboard
reads logs.

The heartbeat is per organisation, because each one polls on its own loop and
one can be broken while the other is healthy.

## Configuration

Everything binds from configuration, and `AddEnvironmentVariables` reads the
`CAT_CARRIER_` prefix with `__` for nesting.

| Key | Default | Meaning |
| --- | --- | --- |
| `Github:WebhookPath` | `/github` | Where webhooks arrive |
| `Github:WebhookSecret` | none | Shared secret for webhook signatures |
| `Github:Runners:Enabled` | `false` | Turn runner polling on |
| `Github:Runners:PollIntervalSeconds` | `30` | Seconds between polls, per org |
| `Github:Runners:LabelAllowlist` | `.*` | Regex over runner labels |
| `Github:Runners:ApiBaseUrl` | `https://api.github.com/` | API root, for GitHub Enterprise Server |
| `Github:Runners:Orgs` | empty | One entry per organisation |

Each org needs either `Token`, or `AppId` plus `InstallationId` plus
`PrivateKey` or `PrivateKeyPath`. A GitHub App needs the organisation
self-hosted runners permission. RS256 is signed with the runtime's own RSA, so
there is no extra package to install, and PKCS#1 and PKCS#8 PEM keys both work.

```json
{
  "Github": {
    "Runners": {
      "Enabled": true,
      "PollIntervalSeconds": 30,
      "LabelAllowlist": "^(parity-macos|parity-macos-large)$",
      "Orgs": [
        {
          "Name": "paritytech",
          "AppId": "123456",
          "InstallationId": "7890123",
          "PrivateKeyPath": "/etc/catcarrier/paritytech.pem"
        }
      ]
    }
  }
}
```

Set `LabelAllowlist` deliberately. Every label a runner carries becomes a
series, and a fleet with one label per machine class will fill Prometheus with
them.

One poll costs one request per 100 runners per org, against an installation
limit of 5000 an hour. Each org polls on its own loop and backs off to eight
intervals when the API fails, so a broken org cannot stall the others or burn
the limit they need.
