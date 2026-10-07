# revit-mcp-server

MCP-сервер для Autodesk Revit. Клиент вроде Claude вызывает инструменты, Node.js пересылает их в открытый Revit, add-in выполняет команды Revit API и возвращает результат.

Подробности потоков, таймаутов и контракта команд — в [ARCHITECTURE.md](ARCHITECTURE.md).

## Как это связано

```
Claude / другой MCP-клиент
        │  MCP по stdin/stdout  или  HTTP /mcp
        ▼
Node.js MCP-сервер          server/
        │  WebSocket, JSON-RPC 2.0
        ▼
RevitConnector              фоновый поток внутри Revit
        │  очередь + ExternalEvent
        ▼
RevitCommandDispatcher      главный поток Revit, Revit API
```

Два направления не совпадают, и это специально.

- Порт слушает Node (`ws://127.0.0.1:8080`). Revit сам подключается к нему и поднимает сокет заново, если связь пропала. Так процесс MCP может уже работать, когда Revit ещё не открыт.
- Команды идут в обратную сторону. Claude вызывает инструмент, Node шлёт JSON-RPC запрос, Revit выполняет его и отвечает с тем же `id`.

Revit API однопоточный. Сокет живёт в фоне, а вызов API попадает на главный поток через `ExternalEvent`. Пока Revit занят модальным окном, команда ждёт не дольше `ExecutionTimeoutMs`.

## Что нужно

- Node.js 20 или новее
- .NET SDK, чтобы собрать add-in (`net48`, x64)
- Autodesk Revit 2022. В решении сейчас конфигурация `Debug R22` / `Release R22`

## Из чего состоит репозиторий

| Папка | Роль |
|---|---|
| `server/` | MCP-сервер на TypeScript. Инструменты, WebSocket-сервер, локальная база снимков |
| `RevitConnector/` | Add-in: сеть, JSON-RPC, лента со статусом связи, перенос вызова на главный поток |
| `RevitCommandDispatcher/` | Команды Revit API. Транспорт их не знает |

Встроенные методы диспетчера: `revit.ping`, `revit.getVersion`, `revit.getMaterialTakeoff`. Дополнительные команды можно подключить DLL через `Commands/commandRegistry.json` рядом с add-in. Файл создаётся при первом запуске, пример в нём выключен.

## Сборка

MCP-сервер:

```bash
cd server
npm install
npm run build
```

`npm run build` очищает `server/build` и компилирует `src` туда. Исходники TypeScript в git не подменяются этим каталогом: `build/` в `.gitignore`. Точка входа для клиента — `server/build/index.js`.

Add-in:

```bash
dotnet build revit-mcp-server.sln -c "Debug R22"
```

Выход коннектора: `RevitConnector/bin/Debug/2022/`. Рядом с `RevitConnector.dll` должны лежать `RevitCommandDispatcher.dll` и остальные зависимости. Манифест `RevitConnector.addin` копируется в этот каталог.

Чтобы Revit подхватил add-in, скопируйте содержимое выходного каталога и `RevitConnector.addin` в `%APPDATA%\Autodesk\Revit\Addins\2022\`.

## Запуск

Один процесс всегда поднимает WebSocket для Revit. Способ принять MCP выбирается флагом.

`npm start` в `server/` (после `npm run build`) слушает HTTP:

- MCP: `http://127.0.0.1:2504/mcp`
- Revit: `ws://127.0.0.1:8080`

Порт MCP меняется через `MCP_PORT`. Это отдельный эндпоинт процесса: остальные пути отвечают 404. Слушает только `127.0.0.1`. Клиент MCP подключается по URL, процесс вы запускаете сами.

Без флага `--http` тот же `node server/build/index.js` остаётся на stdin/stdout. Так клиент может сам запустить процесс. Служебные сообщения пишутся в stderr: в этом режиме stdout занят протоколом MCP.

Пример, когда клиент сам запускает процесс (stdio):

```json
{
  "mcpServers": {
    "revit": {
      "command": "node",
      "args": ["C:/Projects/Programming/My/revit-mcp-server/server/build/index.js"],
      "env": {
        "REVIT_WS_PORT": "8080"
      }
    }
  }
}
```

Пример, когда процесс уже запущен через `npm start`:

```json
{
  "mcpServers": {
    "revit": {
      "url": "http://127.0.0.1:2504/mcp"
    }
  }
}
```

Порт по умолчанию и так `8080`. `REVIT_WS_PORT` нужен, только если 8080 занят. Тот же адрес должен быть в `connectorSettings.json` у add-in (`ServerUri`).

Дальше откройте Revit с загруженным add-in. На вкладке **MCP**, панель **Connection**, кнопка статуса показывает связь с MCP-сервером:

| Цвет | Текст | Смысл |
|---|---|---|
| жёлтый | Connecting | сокет открывается |
| зелёный | Connected | WebSocket к Node открыт |
| оранжевый | Retrying | сервер недоступен, add-in ждёт и пробует снова |
| красный | Offline | цикл соединения остановлен |

**Reconnect** обрывает текущую попытку и подключается сразу, без паузы backoff. Клик по статусу открывает адрес, номер попытки и текст последней ошибки.

Порядок запуска свободный. Если Revit открыт раньше Node, add-in будет переподключаться, пока сервер не появится. Если Node уже слушает порт, связь появится при старте Revit.

## Инструменты

Модули из `server/src/tools/` подхватываются сами: файл должен экспортировать функцию `register...`. `registerTools.ts` в этот список не входит.

| Инструмент | Куда ходит | Что делает |
|---|---|---|
| `say-hello` | никуда | Проверка, что MCP-сервер отвечает |
| `get-revit-version` | Revit | Имя, номер и сборка открытого Revit |
| `store-project-data` | локальная база | Карточка проекта: имя, путь, номер, адрес, заказчик, статус, автор. Revit при этом не читается |
| `extract-material-quantities` | Revit, затем база | Объёмы основных материалов по уровням открытой модели и запись снимка |
| `query-stored-data` | локальная база | Проекты, снимки материалов и сводка. Revit для чтения не нужен |

`extract-material-quantities` смотрит несущие колонны, каркас, фундаменты, перекрытия, стены, лестницы, пандусы, соединения и арматуру. Бетон, сталь, кладка и дерево суммируются по объёму материала. Арматура считается длиной стержней, отдельно от объёма бетона. Краска, гипсокартон, стекло и утеплитель в снимок не входят. Объём в м³, длина арматуры и отметка уровня в метрах. Повторный вызов заменяет предыдущий снимок этого проекта.

Имя проекта берётся из заголовка документа. Если карточка в базе называется иначе, передайте `project_name`.

`query-stored-data` принимает `query_type`:

- `all_projects`, `project_by_id`, `project_by_name`
- `materials_by_project_id`, `materials_by_project_name`, `all_materials`
- `stats`

## Локальная база

Снимки лежат в SQLite-файле `server/revit-data.db`. Его нет в git. Каталог `server/build` при каждой сборке удаляется, поэтому база хранится на уровень выше и сборка её не стирает.

База не зеркало модели. В неё попадает только то, что записал `store-project-data` или `extract-material-quantities`. После закрытия Revit `query-stored-data` по-прежнему читает последний снимок.

## Настройки add-in

При первом старте рядом с DLL появляется `connectorSettings.json`.

| Поле | По умолчанию | Зачем |
|---|---|---|
| `ServerUri` | `ws://127.0.0.1:8080` | Куда подключается Revit |
| `ExecutionTimeoutMs` | `60000` | Сколько ждать главный поток Revit |
| `InitialBackoffMs` | `1000` | Первая пауза перед повтором |
| `MaxBackoffMs` | `30000` | Потолок паузы |
| `HeartbeatIntervalMs` | `15000` | Интервал WebSocket ping |
| `HeartbeatTimeoutMs` | `45000` | Не используется, поле оставлено для старых файлов |

Лог add-in: `Logs/revit-connector-YYYYMMDD.log` в том же каталоге, что и DLL.

Тишина без вызовов от Claude соединение не рвёт. Обрыв виден, когда перестают проходить ping или Node закрывает сокет.

## Новая команда Revit

1. Реализовать `IRevitCommand` в `RevitCommandDispatcher/Commands/Builtin/` и зарегистрировать его в `CommandRegistry.RegisterBuiltins()`.
2. Добавить инструмент в `server/src/tools/`, который вызывает `withRevitConnection("method.name", params)`.
3. Пересобрать add-in и `npm run build` в `server/`.

Чужая DLL подключается записью в `Commands/commandRegistry.json`, без правки этого репозитория. Ошибка одной такой записи не останавливает add-in.
