# C# Runner P.O.C. — C# in een Hyper‑V geïsoleerde ACA container

Korte omschrijving
- Dit repository is een proof‑of‑concept dat aantoont dat C# code uitvoerbaar is binnen een Hyper‑V geïsoleerde Azure Container Apps (ACA) container.
- Het bevat:
  - Een C# runner image die C# scriptbestanden uitvoert met `dotnet-script`. Zie [C# Runner Image/Dockerfile](C%23%20Runner%20Image/Dockerfile) en [`app.MapPost("/runner")`](C%23%20Runner%20Image/program.cs) in [C# Runner Image/program.cs](C%23%20Runner%20Image/program.cs).
  - Een Python backend (Flask) die opdrachten doorstuurt naar de runner pool. Zie [backend/app.py](backend/app.py) en de helperfuncties [`app.get_token`](backend/app.py) en [`post_with_retry`](backend/app.py).
  - Een eenvoudige React frontend (web IDE) om C# code te bewerken en uit te voeren. Zie [React Frontend/src/App.tsx](React%20Frontend/src/App.tsx).

Belangrijke bestanden
- C# runner:
  - [C# Runner Image/Dockerfile](C%23%20Runner%20Image/Dockerfile)
  - [C# Runner Image/program.cs](C%23%20Runner%20Image/program.cs)
  - CI: [C# Runner Image/.github/workflows/build-docker.yml](C%23%20Runner%20Image/.github/workflows/build-docker.yml)
- Backend (Flask):
  - [backend/app.py](backend/app.py)
  - [backend/requirements.txt](backend/requirements.txt)
- Frontend (React):
  - [React Frontend/src/App.tsx](React%20Frontend/src/App.tsx)
  - [React Frontend/package.json](React%20Frontend/package.json)

Quickstart (lokaal)
1. Vereisten
   - Docker
   - Node.js + npm
   - Python 3.10+ (virtuele omgeving aanbevolen)
   - Voor de backend: werkende Azure credentials beschikbaar voor `EnvironmentCredential` (vb. `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_SECRET`) of een andere valide EnvironmentCredential‑bron.

2. Build & run de C# runner image
   ```sh
   # ga naar de image map
   cd "C# Runner Image"
   docker build -t csharp-runner:local .
   # run op poort 6000 (zoals verwacht door de backend)
   docker run --rm -p 6000:6000 csharp-runner:local

   De runner luistert standaard op poort 6000 zoals ingesteld in Dockerfile en program.cs.

3. Start de backend
   ```sh
    cd backend
    python -m venv .venv
    source .venv/bin/activate
    pip install -r requirements.txt
    # Stel POOL_ENDPOINT en Azure env vars in:
    export POOL_ENDPOINT="http://host.docker.internal:6000" # of http://localhost:6000
    export AZURE_CLIENT_ID=...
    export AZURE_TENANT_ID=...
    export AZURE_CLIENT_SECRET=...
    # Start dev server (of gebruik gunicorn in prod)
    python app.py
    # of:
    # gunicorn --bind 127.0.0.1:50505 app:app

De backend stuurt requests naar ${POOL_ENDPOINT}/runner?identifier=.... Zie app.py.
EnvironmentCredential in de backend haalt een token op; zorg dat de Azure credentials correct zijn ingesteld.

4. Start de React frontend
   ```sh
    cd "React Frontend"
    npm install
    npm start

Frontend open: http://localhost:3000
De frontend spreekt de backend aan op http://127.0.0.1:50505/run zoals te zien in App.tsx.


5. Test (curl) voor snelle test (nadat runner en backend draaien):
   ```sh
    curl -v -X POST http://127.0.0.1:50505/run \
    -H "Content-Type: application/json" \
    -d '{"code":"Console.WriteLine(\"Hallo vanuit dotnet-script\");"}'