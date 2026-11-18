# Dev commands. Recipes must work in both sh and cmd.exe (GNU make on Windows falls back to cmd):
# no inline VAR=x, no rm/cp/mkdir -p; use dotnet or node for file operations.

SLN := WgetUi.slnx
APP := src/WgetUi.App/WgetUi.App.csproj
SMOKE := tools/WgetUi.Smoke/WgetUi.Smoke.csproj
ICONGEN := tools/WgetUi.IconGen/WgetUi.IconGen.csproj
# Inno Setup 7 compiler for `make installer`; empty = found by tools/build-installer.ps1 (PATH, per-user, Program Files).
ISCC ?=
ifeq ($(ISCC),)
ISCC_ARG :=
else
ISCC_ARG := -Iscc "$(ISCC)"
endif

.DEFAULT_GOAL := help
.PHONY: help stop dev run build test format check publish installer smoke icons clean

help:
	@echo make dev      - run the app with dotnet watch and hot reload, stops a leftover dev instance first
	@echo make stop     - stop Debug wgetui instances started from this repo, e.g. one left in the tray
	@echo make run      - run the app once in Debug
	@echo make build    - build the solution in Debug
	@echo make test     - run the unit tests
	@echo make format   - apply dotnet format to the solution
	@echo make check    - format check + Release build with warnings as errors + tests
	@echo make publish  - self-contained single-file win-x64 exe in publish/
	@echo make installer - Inno Setup 7 installer in artifacts/, runs publish first, ISCC=path.exe overrides the compiler
	@echo make smoke    - opt-in live check: read-only winget search and upgrade listing
	@echo make icons    - regenerate src/WgetUi.App/Assets/wgetui.ico from the artwork in Icons/IconArt.cs
	@echo make clean    - dotnet clean

stop:
	powershell -NoProfile -ExecutionPolicy Bypass -File tools/stop-dev.ps1

dev: stop
	dotnet watch --project $(APP) run

run: stop
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

installer: publish
	powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-installer.ps1 $(ISCC_ARG)

smoke:
	dotnet run --project $(SMOKE) -c Release

icons:
	dotnet run --project $(ICONGEN) -c Release -- src/WgetUi.App/Assets/wgetui.ico

clean:
	dotnet clean $(SLN)
