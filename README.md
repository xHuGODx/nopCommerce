﻿nopCommerce: free and open-source eCommerce solution
===========

## Hugo Santos Ribeiro 113402

# Structure
- Analysis: [ANALYSIS.md](./ANALYSIS.md)  
  This is my architectural reading of nopCommerce and why I chose this flow and these instrumentation points.
- Report: [REPORT.md](./REPORT.md)  
  This file explains what I actually implemented, which spans and metrics I added, and how the setup works.
- Critique: [CRITIQUE.md](./CRITIQUE.md)  
  This is a short reflection on what worked, what did not work well, and what I would improve.
- Presentation materials: [Presentation](./Presentation)  
  This folder contains the slide deck I used for the presentation.
- Assessment support files: [assessment](./assessment)  
  This folder groups the supporting material for the delivery, such as diagrams, dashboard screenshots, load-test assets, and observability files.
- Source code: [src](./src)  
  This is the original nopCommerce source tree, including the changes made for this assignment.
- Observability configs: [observability](./observability)  
  This folder contains the local OTEL Collector, Prometheus, Grafana, and dashboard configuration.
- Load tests: [loadtests](./loadtests)  
  This folder contains the k6 scripts and shell helpers used to generate successful and failing checkout traffic.

# How to run

From the repository root, the simplest way to run everything is:

1. Start the full local stack:

   ```bash
   docker compose up -d --build
   ```

2. Open the application and tools:
   - Store: `http://localhost`
   - Grafana: `http://localhost:3000`
   - Jaeger: `http://localhost:16686`
   - Prometheus: `http://localhost:9090`

3. Optional: enable fault injection for the demo:

   ```bash
   bash ./loadtests/toggle.sh on
   ```

4. Generate normal checkout traffic:

   ```bash
   bash ./loadtests/load.sh
   ```

5. If needed, generate a controlled failed checkout:

   ```bash
   bash ./loadtests/failure.sh
   ```

6. Inspect the results:
   - In Grafana, open the dashboard `Customer Places an Order`
   - In Jaeger, inspect traces for the service `nopcommerce-web`

This project was mainly tested through Docker, so the Docker-based workflow is the expected way to reproduce the setup.
