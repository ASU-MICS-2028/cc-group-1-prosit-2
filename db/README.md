# db/ — Lab 6 originals and sample data

| File | What it is now |
|---|---|
| `schema.sql`, `rbac.sql` | The Lab 6 schema and roles, by Liza Aikins. **Kept as the original reference.** They are no longer run by hand: the database is created and changed only by the migrations in `backend/Services/Libs/Data/Migrations/Sql/`, where migration 1 is these two files unchanged. |
| `seed.sql` | Synthetic sample data (10 patients, 50 readings, 3 districts). Load it into a database the API has already migrated. A test keeps it loading. |

Do not edit `schema.sql` or `rbac.sql` to change the database; add a migration instead. See [docs/database](../docs/database/README.md) for the diagram, who can do what, and how to add a migration.

Load the sample data into a local database:

```bash
docker exec -i healthconnect-backend-db-1 psql -U healthconnect -d healthconnect -v ON_ERROR_STOP=1 < db/seed.sql
```
