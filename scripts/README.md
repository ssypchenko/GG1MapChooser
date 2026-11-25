# Подготовка ресурсов Panorama

Запускай команды из корня репозитория с GNU Make, Python 3.10+ и Pillow. Если команда интерпретатора называется `python`, добавь `PYTHON=python`.

## Получить исходники

| Источник | Команда |
| --- | --- |
| Конфигурация, карты и изображения на доступном по FTP сервере | `make fetch_ftp` |
| JSON со списком карт и ID Workshop | `make fetch_workshop MAP_LIST=path/to/maps.json` |
| Локальная папка с картами и изображениями | `make fetch_local MAPS=path/to/game/csgo/maps` |

Для FTP скопируй `.env.example` в `.env` и заполни `GGMC_FTP_*`. При пустом пароле скрипт запросит его. Если сервер поддерживает явный FTPS, используй `GGMC_FTP_TLS=1`. Команда читает данные сервера и записывает локальный инвентарь `workshop-maps.json`. Укажи `MAP_ARTIFACT=path/to/file.json`, если нужен другой путь. По умолчанию `.env` и инвентарь игнорируются Git.

Для `fetch_workshop` передай JSON-объект с ключами карт. Поле `display` обязательно, а `mapid` можно опустить.

```json
{
  "gg_giza": { "mapid": "3834969460", "display": "Giza" },
  "de_example": { "display": "Example" }
}
```

## Результат

### Порядок поиска превью

По умолчанию скрипты читают `Panorama/panorama/images/mapPreviews/` из репозитория. Чтобы взять изображения из существующего аддона, передай `PREVIEWS="path/to/addon/panorama/images/mapPreviews"` при вызове Make.

1. `mapPreviews/manualy/<ключ карты>.vtex`, затем `.png`, `.jpg` и `.jpeg`.
2. `mapPreviews/<ключ карты>.vtex`, затем `.png`, `.jpg` и `.jpeg`. Если файла нет, команда пробует свой обычный источник. Сюда же попадут превью, полученные из локальной папки карт, FTP или Workshop.
3. `mapPreviews/defatul.vtex` или `mapPreviews/defatul.png`. Если своей заглушки нет, в комплект попадёт заглушка из репозитория.

Например, `manualy/gg_giza.jpg` получит путь `s2r://panorama/images/mapPreviews/manualy/gg_giza.vtex`. Скрипт конвертирует PNG и JPG в текстуры Panorama. Для готового исходника `.vtex` сохраняется его содержимое, а одноимённые изображения копируются рядом. Текстура должна ссылаться на корректные пути в аддоне.

Порядок разрешается при подготовке ресурсов. После добавления или замены изображения пересобери комплект, скомпилируй аддон и используй новый `GG1MapPreviews.json`.

По умолчанию команды создают новый каталог `dist/map-previews/`. Для повторной сборки укажи свободный путь через `OUT_DIR`.

```text
dist/map-previews/
├── Panorama/
│   └── panorama/
│       ├── layout/custom_game/ggmc_map_vote.xml
│       ├── styles/custom_game/ggmc_map_vote.css
│       └── images/mapPreviews/
│           ├── manualy/
│           ├── defatul.png
│           ├── defatul.vtex
│           └── <map>.png и <map>.vtex
├── GG1MapPreviews.json
└── workshop-previews.json  (для fetch_ftp и fetch_workshop)
```

- `Panorama/panorama/` содержит исходники для существующего аддона. Имена файлов превью совпадают с ключами карт. В CSS уже есть ссылки на подготовленные изображения.
- `GG1MapPreviews.json` сопоставляет ключ карты с клиентским путём `s2r://panorama/…`. Для карт без изображения указывается общая заглушка. Она также используется в игре для неизвестных карт и при отсутствующем или повреждённом JSON.
- `workshop-previews.json` содержит диагностические данные о поиске, включая найденные пути и статусы. Для аддона достаточно папки `panorama/`.
- `make pack MAPS=path/to/maps ZIP=path/to/new.zip` создаёт ZIP с исходниками Panorama и таблицей превью из локальной папки карт.

Скрипты читают источники и формируют перечисленные файлы в выбранном выходном каталоге. `make plugin` отдельно собирает DLL в `dist/GG1MapChooser/`.

## Собрать аддон и проверить

1. Объедини содержимое `Panorama/panorama/` с папкой `panorama/` в **content**-каталоге существующего аддона. Скомпилируй его в CS2 Workshop Tools на Windows и опубликуй обновление своим обычным способом.
2. Для локальной проверки вручную установи DLL, `GG1MapChooser.deps.json`, `ggmcAPI.dll` и `lang/` в каталог плагина. Положи таблицу превью рядом с `GG1MapChooser.json`.
3. Подключи аддон на клиенте и локальном сервере. Выставь режим `panorama` для нужного меню и проверь голосование командой `ggmc_mapvote_start 45` в серверной консоли. При `ShowMapThumbnails: true` проверь изображение и заглушку. При `ShowMapThumbnails: false` проверь, что названия занимают освободившееся место. Повтори с `DisplayVotesCount` в обоих состояниях.
