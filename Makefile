# Dev commands. Recipes must work in both sh and cmd.exe (GNU make on Windows falls back to cmd):
# no inline VAR=x, no rm/cp/mkdir -p; use dotnet or node for file operations.

SLN := WgetUi.slnx
APP := src/WgetUi.App/WgetUi.App.csproj
SMOKE := tools/WgetUi.Smoke/WgetUi.Smoke.csproj

.DEFAULT_GOAL := help
.PHONY: help dev run build test format check publish smoke clean

help:
	@echo make dev      - run the app with dotnet watch (hot reload)
	@echo make run      - run the app once (Debug)
	@echo make build    - build the solution (Debug)
	@echo make test     - run the unit tests
	@echo make format   - apply dotnet format to the solution
	@echo make check    - format check + Release build with warnings as errors + tests
	@echo make publish  - self-contained single-file win-x64 exe in publish/
	@echo make smoke    - opt-in live check: read-only winget search and upgrade listing
	@echo make clean    - dotnet clean

dev:
	dotnet watch --project $(APP) run

run:
	dotnet run --project $(APP)

build:
	dotnet build $(SLN)

test:
	dotnet test --solution $(SLN)

format:
	dotnet format $(SLN)

check:
	dotnet format $(SLN) --verify-no-changes
	dotnet build $(SLN) -c Release -warnaserror
	dotnet test --solution $(SLN) -c Release --no-build

publish:
	dotnet publish $(APP) -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish

smoke:
	dotnet run --project $(SMOKE) -c Release

clean:
	dotnet clean $(SLN)
