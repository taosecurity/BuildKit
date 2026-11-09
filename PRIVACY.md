# Privacy

BuildKit is a library that runs inside your own builder program, on your own computer. It collects no data and
sends nothing anywhere.

## What it does not do

- It makes no network connections.
- It has no telemetry, analytics, crash reporting or update checks.
- It stores no settings, logs or caches of its own.

## What it reads

Only the files your builder points it at:

- master plugins (`.esm`) in the game's Data folder and any extra folders you pass to `GameData`;
- the ID lock file you pass to `IdLock.Apply`;
- plugins you open with `GameData.OpenPlugin`, such as the one you just wrote.

## What it writes

- the plugin (and, for a localized plugin, its `Strings` files) in the output folder you pass to `PluginWriter.Write`;
- the ID lock file you pass to `IdLock.Apply`, when a record is added.

It prints progress lines to the console.

## Other programs it starts

When the optional gap check script is found (see the README), BuildKit runs `python` on that script, on your computer,
with the path of the plugin just written and your Data folder. Nothing else is started.

## Personal data

BuildKit handles none. The only personal detail that can end up in a plugin is the author name and description your
builder passes to `Plugin.New`; those go into the plugin's header, as in any plugin.

## Downloading the package

Downloading BuildKit from nuget.org or GitHub is covered by those sites' own privacy policies
([NuGet](https://www.nuget.org/policies/Privacy), [GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement)).
BuildKit's author receives only the public download counts those sites show.

## Questions

Open an issue at https://github.com/taosecurity/BuildKit/issues.
