# Agent instructions

## Running the tests

Run the tests as CONTRIBUTING.md's "Running the tests" section shows, with `SPECIFICATION_TEST_SQL_SERVER` set to the `spec-sql` container's connection string. Several agent sessions share that container, and every test run resets the same databases. Wrap each `dotnet test` in the shared lock, so one run waits for the other:

```bash
flock ~/.cache/specification-spec-sql.lock dotnet test ci.slnf --framework net10.0
```

## Agent skills

### Issue tracker

Issues live in GitHub Issues on bandoyer/OpenSpecification, never on ardalis/Specification. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root, created when a term or decision first needs them. See `docs/agents/domain.md`.
