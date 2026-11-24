## What and why

<!-- What does this change, and which document or file did it come from? -->

## Checklist

- [ ] `dotnet build Arkki.slnx -c Release -warnaserror` is clean
- [ ] `dotnet run -c Release --project tests/Arkki.Tests` passes
- [ ] New behaviour has a test; a reader fix has the smallest file that showed the problem
- [ ] No new dependency in `Arkki.Pdf`, no native code, no platform-specific API
- [ ] Public API has XML documentation, and `README.md` and `CHANGELOG.md` say what changed
- [ ] No test file holds personal or confidential data
