# Contributing to OpenSpecification

We love your input! We want to make contributing to this project as easy and transparent as possible, whether it's:

- Reporting a bug
- Discussing the current state of the code
- Submitting a fix
- Proposing new features

## We Develop with GitHub

Obviously...

## We Use Pull Requests

Mostly. But pretty much exclusively for non-maintainers. You'll need to fork the repo in order to submit a pull request. Here are the basic steps:

1. Fork the repo and create your branch from `main`.
2. If you've added code that should be tested, add tests.
3. If you've changed APIs, update the documentation.
4. Ensure the test suite passes.
5. Make sure your code lints.
6. Issue that pull request!

## Running the tests

The EF6 and EF Core tests need SQL Server. On Windows they use LocalDB. Elsewhere they start a SQL Server container through Docker.

To use a SQL Server you already run, set `SPECIFICATION_TEST_SQL_SERVER` to its connection string. The tests create their own databases on it. For example, with a container on this machine:

```bash
docker run -d --name spec-sql -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=P@ssW0rd!' -p 127.0.0.1:1433:1433 mcr.microsoft.com/mssql/server:2022-latest
export SPECIFICATION_TEST_SQL_SERVER='Server=localhost,1433;User Id=sa;Password=P@ssW0rd!;TrustServerCertificate=True'
dotnet test ci.slnf --framework net10.0
```

Only one test run at a time should use the server, because each run resets the same databases.

## Ask before adding a pull request

You can just add a pull request out of the blue if you want, but it's much better etitquette (and more likely to be accepted) if you open a new issue or comment in an existing issue stating you'd like to make a pull request.

## Getting Started

Look for [issues marked with 'help wanted'](https://github.com/bandoyer/OpenSpecification/issues?q=is%3Aissue+is%3Aopen+label%3A%22help+wanted%22) to find good places to start contributing.

## Any contributions you make will be under the MIT Software License

In short, when you submit code changes, your submissions are understood to be under the same [MIT License](http://choosealicense.com/licenses/mit/) that covers this project.

## Report bugs using Github's [issues](https://github.com/bandoyer/OpenSpecification/issues)

We use GitHub issues to track public bugs. Report a bug by [opening a new issue](https://github.com/bandoyer/OpenSpecification/issues/new/choose); it's that easy!

## Releasing

The maintainer releases. Each package's `<Version>` in its `.csproj` sets the version that ships, so it has to match the release.

1. Run the "Release to NuGet" workflow by hand from the Actions tab. It builds, tests and packs at `main`, pushes nothing, and keeps the packages as the run's `packages` artifact.
2. Publish a GitHub release whose tag is the version, such as `v10.0.0`. The same workflow runs again and pushes the packages to nuget.org.

The push uses [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), so the repo stores no NuGet key. It needs a trusted publishing policy on nuget.org for this repo and the workflow file `release.yml`, and the repo variable `NUGET_USER` set to the nuget.org username that owns the packages.
