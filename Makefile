PYTHON ?= python3
DOTNET ?= dotnet
PLUGIN_OUT ?= dist/GG1MapChooser
ENV_FILE ?= .env
MAP_ARTIFACT ?= workshop-maps.json
OUT_DIR ?= dist/map-previews
ZIP ?= ggmc-panorama.zip
PREVIEWS ?= Panorama/panorama/images/mapPreviews

.PHONY: help fetch fetch_ftp fetch_workshop fetch_local pack plugin

help:
	@echo 'make plugin [PLUGIN_OUT=dist/GG1MapChooser]'
	@echo 'make fetch_ftp [ENV_FILE=.env] [MAP_ARTIFACT=workshop-maps.json]'
	@echo 'make fetch_workshop MAP_LIST=path/to/GGMCmaps.json'
	@echo 'make fetch_local MAPS=/path/to/maps'
	@echo 'make pack MAPS=/path/to/maps [ZIP=ggmc-panorama.zip]'
	@echo 'Preview commands accept PREVIEWS=/path/to/addon/panorama/images/mapPreviews'

plugin:
	"$(DOTNET)" build GG1MapChooser.csproj -c Release --output "$(PLUGIN_OUT)"

fetch: fetch_ftp

fetch_ftp:
	"$(PYTHON)" scripts/Fetch-WorkshopPreviews.py --artifact "$(MAP_ARTIFACT)" --env-file "$(ENV_FILE)" --panorama --output-dir "$(OUT_DIR)" --preview-root "$(PREVIEWS)"

fetch_workshop:
	$(if $(strip $(MAP_LIST)),,$(error Set MAP_LIST to the map-list JSON path))
	"$(PYTHON)" scripts/Fetch-WorkshopPreviews.py --map-list "$(MAP_LIST)" --panorama --output-dir "$(OUT_DIR)" --preview-root "$(PREVIEWS)"

fetch_local:
	$(if $(strip $(MAPS)),,$(error Set MAPS to the server maps directory))
	"$(PYTHON)" scripts/Pack-MapVoteAddon.py "$(MAPS)" --output-dir "$(OUT_DIR)" --preview-root "$(PREVIEWS)"

pack:
	$(if $(strip $(MAPS)),,$(error Set MAPS to the server maps directory))
	"$(PYTHON)" scripts/Pack-MapVoteAddon.py "$(MAPS)" --output "$(ZIP)" --preview-root "$(PREVIEWS)"
