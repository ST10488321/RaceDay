# RaceDay API – Part 2 (PROG6212)

A RESTful ASP.NET Core Web API (.NET 8) for managing running, walking and cycling events.
It uses Entity Framework Core (Code-First) with SQL Server, session-based authentication, role-based access (Organiser / Participant), Swagger, xUnit tests and a GitHub Actions CI pipeline.

## Links
- GitHub repo: https://github.com/ST10488321/Programming
- YouTube demo (unlisted): **PASTE LINK HERE**

## Project structure
```
Part2-RaceDay-API/
  RaceDay.Api/        Controllers, Models, Data (DbContext), Dtos, Security, Services
  RaceDay.Tests/      xUnit integration/unit tests
.github/workflows/    part2-ci.yml (build + test on every push)
```

## Setup and run
1. Install Visual Studio 2022 with the ASP.NET workload, .NET 8 SDK and SQL Server LocalDB.
2. Clone the repo and open `Part2-RaceDay-API/RaceDay.sln`.
3. Check the connection string in `RaceDay.Api/appsettings.json` (default uses LocalDB).
4. Create the database (Package Manager Console, default project RaceDay.Api):
   `Update-Database`  (or the API applies migrations automatically on start).
5. Press **F5** on RaceDay.Api. Swagger UI opens at `http://localhost:5080/swagger`.
6. Run tests: `dotnet test` or Test Explorer in Visual Studio.

## The two roles
| Role | Can do |
|------|--------|
| **Organiser** | Create / update / delete **their own** events; add / edit / delete categories; view all enrolments for their events; change enrolment status; capture finish times and positions; view results of their events. |
| **Participant** | View events and categories; enrol in an event by choosing a category; cancel own enrolment; view own enrolments and results; view/update own profile. |

Both roles can register, log in and view/update their own profile.

**How Organiser registration is managed:** anyone can pick a role, but registering as *Organiser* requires a secret `organiserCode` (set in `appsettings.json` → `Registration:OrganiserCode`, default `RACEDAY-ORG-2026`). This stops random users from making themselves Organisers.

## Authentication and security
- `POST /api/auth/login` creates a **server-side session** that stores the user's Id and Role; the browser keeps only a session cookie.
- Non-public endpoints use `[SessionAuthorize]` → **401** if not logged in, **403** if the role is wrong.
- Passwords are hashed with **PBKDF2-SHA256 + random salt**; plain passwords are never stored.

## Endpoints
| Method | Route | Role |
|---|---|---|
| POST | /api/auth/register | Public |
| POST | /api/auth/login | Public |
| POST | /api/auth/logout | Any |
| GET / PUT | /api/users/me | Any |
| GET | /api/events, /api/events/{id}, /api/events/{id}/categories | Any |
| POST / PUT / DELETE | /api/events, /api/events/{id} | Organiser (owner) |
| POST / PUT / DELETE | /api/categories, /api/categories/{id} | Organiser (owner) |
| GET | /api/enrolments | Own (Participant) / their events (Organiser) |
| POST / DELETE | /api/enrolments, /api/enrolments/{id} | Participant |
| PUT | /api/enrolments/{id} | Organiser |
| GET | /api/enrolments/{id}/result | Participant (own) / Organiser |
| GET | /api/results/me | Participant |
| GET | /api/events/{id}/results | Organiser |
| POST / PUT / DELETE | /api/results, /api/results/{id} | Organiser |

## Testing the API in Swagger
1. `POST /api/auth/register` (Organiser needs the organiserCode), then `POST /api/auth/login`.
2. Swagger UI automatically sends the session cookie for later "Try it out" calls.

## Unit tests
`RaceDay.Tests` runs the real API in memory (in-memory database) and covers registration, login, hashed passwords, 401/403 enforcement, event CRUD, categories, enrolments and results – both success and failure scenarios.

## CI/CD
GitHub Actions workflow `.github/workflows/part2-ci.yml` restores, builds and runs all tests on every push.

![Green build](docs/ci-green-build.png)
