# PrReviewBot

PrReviewBot is a GitHub Action that automatically reviews pull requests: it fetches the PR diff via the GitHub API, runs Roslyn and StyleCop.Analyzers against the changed files, sends the resulting diagnostics to a free LLM via OpenRouter to produce readable review comments, and posts them back as a single inline-comment review on the pull request.

## What it does

On every PR (opened or updated):

1. Fetches the diff and each changed file's content via the GitHub API.
2. Runs Roslyn + StyleCop.Analyzers against the changed `.cs` files to catch style/quality issues.
3. Sends the diagnostics and diff context to a free model on OpenRouter to turn raw rule violations into readable, contextual review comments.
4. Filters everything to lines actually present in the diff.
5. Posts a single GitHub review with inline comments - visible in the PR's "Files changed" tab, exactly like a human reviewer's comments. If nothing is found, no review is posted.

## Required secrets

Adopting repos need to configure two secrets (Settings -> Secrets and variables -> Actions):

| Secret               | Description                                                                                           |
| -------------------- | ----------------------------------------------------------------------------------------------------- |
| `GITHUB_TOKEN`       | Provided automatically by GitHub Actions - just pass `secrets.GITHUB_TOKEN` through, no setup needed. |
| `OPENROUTER_API_KEY` | An API key from [openrouter.ai](https://openrouter.ai/) used to call a free model.                    |

## Local development

Running `PrReviewBot.Cli` outside of a GitHub Action still needs `GITHUB_TOKEN` and
`OPENROUTER_API_KEY`. Instead of exporting them by hand, copy [.env.example](.env.example) to
`.env` at the repo root and fill in real values:

```sh
cp .env.example .env
```

`.env` is gitignored and is only a local convenience - the CLI reads it once at startup and
only fills in variables that aren't already set, so real environment variables (and GitHub
Actions secrets in CI) always win. It's also where you can override `OPENROUTER_MODEL` or set
`GITHUB_REPOSITORY` so you can skip the `--owner`/`--repo` CLI flags locally.

## Usage

Add a workflow like this to your repo (see [.github/workflows/pr-review.yml](.github/workflows/pr-review.yml) for the full version):

```yaml
name: PR Review Bot

on:
  pull_request:
    types: [opened, synchronize]

permissions:
  pull-requests: write
  contents: read

jobs:
  review:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: your-org/pr-review-bot@v1
        with:
          github-token: ${{ secrets.GITHUB_TOKEN }}
          openrouter-key: ${{ secrets.OPENROUTER_API_KEY }}
```

### A note on fork PRs

The snippet above uses `pull_request`, which is safe by default but runs with **no access to secrets** for PRs opened from forks - so the bot won't be able to authenticate with OpenRouter (or, in stricter setups, GitHub) on those PRs.

If your repo accepts external contributions and you want the bot to run on fork PRs too, switch the trigger to `pull_request_target` instead. That event runs with the base repo's secrets and permissions, **but it also checks out and executes the workflow file from the base branch, not from the fork** - so a malicious fork PR cannot rewrite the workflow to exfiltrate your secrets just by editing its own `.yml`. Still, be careful: `pull_request_target` runs before any review, so avoid checking out and _executing_ fork code (e.g. running its build scripts) in the same job unless you've explicitly sandboxed that step.

## License

[MIT](LICENSE)
