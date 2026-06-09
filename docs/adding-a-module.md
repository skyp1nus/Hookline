# Adding a module

The whole architecture exists so that adding the *next* tool is cheap and never touches
the previous ones. The cost of a new module is exactly:

- **1 backend module project** (`Hookline.Modules.Foo`)
- **1 frontend feature area** (`web/src/app/(app)/<area>/…`)
- **1 nav entry** (`web/src/lib/nav.ts`)

…with **zero edits to existing modules**. The `Hookline.Modules.Sample` module is the
worked reference — copy its shape.

## Backend

1. **Create the project** `src/Modules/Hookline.Modules.Foo` (references only
   `Hookline.SharedKernel`, plus `Hookline.Infrastructure` *only if* it needs a shared
   implementation type). Internal folders: `Domain / Features / Infrastructure / Endpoints / Jobs`.
   Add it to `Hookline.slnx`.

2. **Add `FooDbContext`** deriving `HooklineDbContext`, mapped to schema `foo` with its
   own migrations-history table:
   ```csharp
   options
     .UseNpgsql(postgres, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", FooDbContext.SchemaName))
     .UseSnakeCaseNamingConvention();
   ```
   Add an `IDesignTimeDbContextFactory<FooDbContext>` (see `SampleDbContextFactory`) and
   the initial migration:
   ```bash
   dotnet ef migrations add Initial \
     --project src/Modules/Hookline.Modules.Foo \
     --startup-project src/Hookline.Host \
     --context Hookline.Modules.Foo.FooDbContext -o Migrations
   ```

3. **Implement `FooModule : IModule`** — `RegisterServices` (register the context + jobs),
   `MapEndpoints` (under `/api/foo`), `RegisterJobs`, `RequiredConnections`, and
   `Migrate` (return the context). See `SampleModule`.

4. **Add the module to the host's explicit list** — one line in `Program.cs`:
   ```csharp
   var modules = new List<IModule> { new SampleModule(), new FooModule() };
   ```
   The host migrates its schema (under the advisory lock), maps its endpoints and
   registers its jobs automatically.

5. **Connections:** if the module needs a *new* connection type, add it to the
   Connections subsystem (OAuth flow + encrypted token storage + a typed accessor in
   `SharedKernel`). Otherwise reuse `ISlackConnections` / `IGoogleConnections`.

6. **Tests:** add unit tests; the architecture-boundary tests
   (`tests/Hookline.ArchitectureTests`) cover the new module automatically — add its
   assembly to `ModuleAssemblies` so the "no module references another module" rule
   includes it.

**Boundaries enforced by the build:** no module references another module; modules
reference only `SharedKernel` (+ allowed Infrastructure); `Domain` folders have no infra
dependencies. An illegal reference fails CI.

## Frontend

7. **Add the feature area** `web/src/app/(app)/<area>/…` (pages render only content — the
   shell, header and container come from the `(app)` layout). Co-locate components/hooks.
   Keep a data-hook seam (a `use…()` hook per resource) so Phase 1/2 swaps hook internals
   for real API calls without changing components.

8. **Add one entry to `web/src/lib/nav.ts`** — a leaf (or a collapsible tool with leaves)
   with its route id, path, label, lucide icon and `module` mapping. That single entry
   drives the sidebar, breadcrumbs and ⌘K palette.

Done — no existing module changes.
</content>
