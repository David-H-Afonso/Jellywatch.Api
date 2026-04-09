# Jellywatch API

RESTful API for tracking Jellyfin watch activity with automatic metadata enrichment from TMDB, TVMaze, and OMDb.

## Features

- **Jellyfin Integration** — Automatic sync via webhooks and polling
- **Multi-User & Multi-Profile** — JWT authentication with per-user profiles
- **Metadata Enrichment** — TMDB (primary), TVMaze (air times), OMDb (ratings)
- **Watch Tracking** — Episode/movie states, watch history, user ratings
- **Statistics** — Year-in-review Wrapped, calendar view, upcoming episodes
- **Asset Proxy** — Local caching and serving of poster/backdrop images
- **Import/Export** — CSV/Trakt data import for migration
- **Translations** — Multi-language metadata from TMDB
- **Genre Tracking** — Genre breakdown and monthly insights

## Tech Stack

- **.NET 9.0** — ASP.NET Core Web API
- **Entity Framework Core 9.0** — SQLite provider
- **JWT Authentication** — BCrypt password hashing
- **Swagger/OpenAPI** — via Swashbuckle

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- TMDB API key (recommended)
- Jellyfin server (optional, for auto-sync)
- OMDb API key (optional, for IMDb/RT ratings)

## Installation

```bash
cd Jellywatch.Api
cp .env.example .env
# Edit .env with your API keys
dotnet restore
dotnet ef database update
```

## Development

```bash
dotnet run
# API available at https://localhost:5001
# Swagger UI at https://localhost:5001/swagger
```

## Production (Docker)

```bash
docker build -t jellywatch-api .
docker run -p 5000:5000 -v jellywatch-data:/app/data jellywatch-api
```

See the root `docker-compose.casaos.yml` for CasaOS deployment.

## API Endpoints

### Authentication

| Method | Route                | Description       |
| ------ | -------------------- | ----------------- |
| POST   | `/api/auth/register` | Register new user |
| POST   | `/api/auth/login`    | Login             |
| POST   | `/api/auth/refresh`  | Refresh JWT token |

### Series

| Method | Route                               | Description                          |
| ------ | ----------------------------------- | ------------------------------------ |
| GET    | `/api/series/{profileId}`           | List series (sort, filter, paginate) |
| GET    | `/api/series/{profileId}/{id}`      | Series detail with seasons/episodes  |
| GET    | `/api/series/credits/{mediaItemId}` | Cast/crew credits                    |

### Movies

| Method | Route                               | Description                          |
| ------ | ----------------------------------- | ------------------------------------ |
| GET    | `/api/movies/{profileId}`           | List movies (sort, filter, paginate) |
| GET    | `/api/movies/{profileId}/{id}`      | Movie detail                         |
| GET    | `/api/movies/credits/{mediaItemId}` | Cast/crew credits                    |

### Stats

| Method | Route                             | Description               |
| ------ | --------------------------------- | ------------------------- |
| GET    | `/api/stats/{profileId}/wrapped`  | Year-in-review statistics |
| GET    | `/api/stats/{profileId}/calendar` | Monthly calendar view     |
| GET    | `/api/stats/{profileId}/upcoming` | Upcoming episodes         |

### Profiles

| Method | Route                | Description        |
| ------ | -------------------- | ------------------ |
| GET    | `/api/profiles`      | List user profiles |
| POST   | `/api/profiles`      | Create profile     |
| PUT    | `/api/profiles/{id}` | Update profile     |

### Media Search

| Method | Route                     | Description           |
| ------ | ------------------------- | --------------------- |
| GET    | `/api/mediasearch/search` | Search TMDB for media |
| POST   | `/api/mediasearch/add`    | Add media to library  |

### Assets

| Method | Route                             | Description          |
| ------ | --------------------------------- | -------------------- |
| GET    | `/api/asset/{mediaItemId}/{type}` | Proxied media images |

### Admin

| Method | Route                                   | Description           |
| ------ | --------------------------------------- | --------------------- |
| GET    | `/api/admin/users`                      | List users            |
| POST   | `/api/admin/media/refresh-all-metadata` | Bulk metadata refresh |
| POST   | `/api/admin/media/refresh-all-images`   | Bulk image refresh    |

### Data Import

| Method | Route                      | Description        |
| ------ | -------------------------- | ------------------ |
| POST   | `/api/data/import/preview` | Preview CSV import |
| POST   | `/api/data/import/confirm` | Execute import     |

## Configuration

Configuration via `appsettings.json` or environment variables:

| Setting                         | Description          |
| ------------------------------- | -------------------- |
| `JellyfinSettings:BaseUrl`      | Jellyfin server URL  |
| `JellyfinSettings:ApiKey`       | Jellyfin API key     |
| `TmdbSettings:ApiKey`           | TMDB API key         |
| `OmdbSettings:ApiKey`           | OMDb API key         |
| `JwtSettings:SecretKey`         | JWT signing key      |
| `DatabaseSettings:DatabasePath` | SQLite database path |

## Project Structure

```
Jellywatch.Api/
├── Configuration/     # App settings models
├── Contracts/         # DTOs (request/response models)
├── Controllers/       # API endpoints
├── Domain/            # Entity models
├── Helpers/           # Utility classes
├── Infrastructure/    # DbContext, EF configuration
├── Middleware/        # JWT, error handling
├── Migrations/        # EF Core migrations
├── Services/
│   ├── Assets/        # Image proxy & caching
│   ├── Auth/          # JWT token service
│   ├── Jellyfin/      # Jellyfin API client
│   ├── Metadata/      # TMDB, TVMaze, OMDb clients
│   └── Sync/          # Watch event orchestration
└── Program.cs         # App entry point
```

## License

Proprietary — All rights reserved.
