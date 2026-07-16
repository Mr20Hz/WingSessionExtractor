# Contributing

Run before committing:

```bash
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test -c Release
```

Do not commit private recordings. Use synthetic WAV fixtures in tests.
