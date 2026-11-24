# ReLevel

Плагин Revit (2025 и новее), который снимает зависимости элементов и видов от уровня, чтобы уровень можно было удалить без потери информации в модели.

Требования к поведению: [docs/SPEC.md](docs/SPEC.md).

## Сборка

Нужны .NET SDK 8 или новее и установленный Revit 2025 (из него берётся API).

```powershell
dotnet build ReLevel.sln -c Release
```

Если Revit установлен не в `C:\Program Files\Autodesk\Revit 2025`, укажите путь: `-p:RevitInstallDir=<путь>`.

Результат:

```text
bin/2025+/
  ReLevel.addin
  ReLevel/ReLevel.Revit.dll
```

## Установка

При закрытом Revit скопируйте содержимое `bin/2025+` в `%APPDATA%\Autodesk\Revit\Addins\<год>`. Команда ReLevel открывает закрепляемую панель.

## Структура

- `src/ReLevel.Revit/ReLevelApplication.cs`, `ReLevelCommand.cs` — регистрация панели и вход в команду.
- `Transfer/` — кейсы переноса элементов, пересоздание видов, удаление, транзакции.
- `Logic/` — расчёты без Revit API.
- `UI/` — закрепляемая панель WPF и диалоги.
- `Inspection/` — инспектор элементов.
- `Localization/` — русский и английский интерфейс.
