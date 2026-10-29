using System.ComponentModel;

namespace ReLevel.Revit.UI.Prototype;

internal sealed class PrototypeText(bool english) : INotifyPropertyChanged
{
    private bool english = english;
    public bool English
    {
        get => english;
        set { if (english == value) return; english = value; PropertyChanged?.Invoke(this, new("Item[]")); }
    }
    public string this[string key] => English ? Labels[key].En : Labels[key].Ru;
    public event PropertyChangedEventHandler? PropertyChanged;
    private static readonly Dictionary<string, (string Ru, string En)> Labels = new()
    {
        ["LiveTitle"] = ("ReLevel", "ReLevel"),
        ["Workspace"] = ("ИНСТРУМЕНТЫ МОДЕЛИ", "MODEL TOOLS"),
        ["ToggleMenu"] = ("Свернуть / развернуть меню", "Collapse / expand menu"),
        ["SelectAll"] = ("Выбрать / снять выбор всех видимых строк", "Select / unselect all visible rows"),
        ["Search"] = ("Поиск…", "Search…"),
        ["Filter"] = ("Фильтр", "Filter"),
        ["Matches"] = ("Совпадений", "Matches"),
        ["Previous"] = ("Предыдущее совпадение (Shift+Enter)", "Previous match (Shift+Enter)"),
        ["Next"] = ("Следующее совпадение (Enter)", "Next match (Enter)"),
        ["Title"] = ("ReLevel · Прототип интерфейса", "ReLevel · UI Prototype"),
        ["Prototype"] = ("ПРОТОТИП ИНТЕРФЕЙСА", "UI PROTOTYPE"),
        ["Safe"] = ("Только чтение уровней • Модель не изменяется", "Level data only • Model remains unchanged"),
        ["Unavailable"] = ("Функция пока не подключена", "This function is not connected yet"),
        ["Distribute"] = ("Распределение по уровням", "Distribute by level"),
        ["DistributeDescription"] = ("Подготовка модели к удалению ненужных уровней путём распределения элементов по сохраняемым уровням", "Prepare the model for removal of unnecessary levels by distributing elements to retained levels"),
        ["DistributeHelp"] = ("Отметьте хотя бы один сохраняемый уровень. Элементы распределяются к ближайшему сохраняемому уровню снизу по высоте; ниже всех уровней — к самому нижнему. Защита сохраняет собственные привязки уровня. Уровни и виды не удаляются.", "Keep at least one level. Elements are assigned to the nearest retained level below their elevation, or the lowest retained level if none is below. Protection preserves a level’s own associations. Levels and views are not deleted."),
        ["Keep"] = ("Оставить уровень", "Keep level"),
        ["Protect"] = ("Не переносить элементы с этого уровня", "Do not move elements from this level"),
        ["Name"] = ("Название уровня", "Level name"),
        ["ModelCountsHelp"] = ("В скобках — элементы существующих кейсов во всей модели, включая закреплённые и недоступные для записи. Двухуровневый элемент учитывается только по нижней привязке; каждый ID считается один раз. * — неполный счётчик из-за ошибок анализа. Столбец «На виде» в Color относится только к активному виду.", "Parentheses show elements matching existing cases across the model, including pinned and read-only elements. Multi-level elements count only at their base level; each ID counts once. * marks an incomplete count due to analysis errors. Color’s In view column refers only to the active view."),
        ["Elevation"] = ("Отметка, м", "Elevation, m"),
        ["ElevationHelp"] = ("Отметки относительно начала проекта, в метрах. ID — в подсказке названия.", "Elevations relative to the project origin, in metres. Hover over a name for its ID."),
        ["Colors"] = ("Окраска по уровням", "Color by level"),
        ["ColorsDescription"] = ("Настройка цветов и скрытия уровней для визуального анализа модели", "Configure level colors and visibility for visual model analysis"),
        ["ColorsHelp"] = ("Выберите уровни, настройте цвет или скрытие и нажмите «Применить». Сброс восстанавливает исходную графику активного вида.", "Select levels, choose colors or hiding, then Apply. Reset restores the original graphics of the active view."),
        ["Selected"] = ("Выбрать", "Select"),
        ["Color"] = ("Цвет", "Color"),
        ["Hide"] = ("Скрыть", "Hide"),
        ["Hidden"] = ("Скрытие", "Hidden"),
        ["Transfer"] = ("Перенос", "Transfer"),
        ["TransferDescription"] = ("Перенос элементов и пересоздание видов на другом уровне", "Transfer elements and recreate views on another level"),
        ["TransferHelp"] = ("Выберите исходный и целевой уровни, затем отметьте элементы или виды для операции.", "Choose source and target levels, then check the elements or views to process."),
        ["Source"] = ("Исходный уровень", "Source level"),
        ["Target"] = ("Целевой уровень", "Target level"),
        ["Elements"] = ("Элементы", "Elements"),
        ["Views"] = ("Виды", "Views"),
        ["NotLoaded"] = ("Данные модели в прототипе не загружаются", "Model data is not loaded in the prototype"),
        ["Move"] = ("Перенести элементы", "Transfer elements"),
        ["Recreate"] = ("Пересоздать виды", "Recreate views"),
        ["Debug"] = ("Отладка", "Debug"),
        ["DebugDescription"] = ("Инструменты выбора и диагностики элементов", "Element selection and diagnostic tools"),
        ["DebugHelp"] = ("Выбор элементов по кейсу, добавление ID в Transfer и инспектор модели.", "Select elements by case, add IDs to Transfer and inspect model elements."),
        ["Settings"] = ("Настройки", "Settings"),
        ["SettingsDescription"] = ("Язык интерфейса", "Interface language"),
        ["SettingsHelp"] = ("Выберите язык. Тексты изменятся сразу, а выбор в таблицах сохранится до закрытия окна.", "Choose a language. Text updates immediately; table choices persist until the window closes."),
        ["Language"] = ("Язык", "Language"),
        ["Apply"] = ("Применить", "Apply"),
        ["Reset"] = ("Сбросить", "Reset"),
        ["Count"] = ("На виде", "In view"),
        ["ResetColorsHelp"] = ("Восстановить исходную графику текущего вида. После закрытия используйте Undo Revit.", "Restore the original graphics of the current view. After closing, use Revit Undo."),
        ["SelectByCase"] = ("Выделить все элементы по…", "Select all elements by…"),
        ["AddById"] = ("Добавить по ID", "Add by ID"),
        ["Inspector"] = ("ReLevel инспектор", "ReLevel Inspector"),
        ["SelectByCaseHelp"] = ("Выделить в модели все элементы выбранного кейса на любых уровнях.", "Select all elements matching a case across the model."),
        ["AddByIdHelp"] = ("Добавить ID в таблицу Transfer. Используется выбранный там исходный уровень.", "Add IDs to Transfer using the source level selected there."),
        ["InspectorHelp"] = ("Закрыть окно, выбрать элемент и дописать снимок в ELEMENTS.md.", "Close the window, pick an element and append its snapshot to ELEMENTS.md."),
        ["RunDistribution"] = ("Распределить элементы", "Distribute elements"),
        ["DistributionComplete"] = ("Распределение завершено.", "Distribution completed."),
        ["DistributionStopped"] = ("Распределение остановлено.", "Distribution stopped."),
        ["Empty"] = ("В документе нет уровней", "The document has no levels"),
        ["PickColor"] = ("Выбор цвета", "Choose color"),
        ["ColorHelp"] = ("Выберите тон и цвет на палитре, образец или введите #RRGGBB.", "Choose a hue and color in the palette, a swatch, or enter #RRGGBB."),
        ["InvalidColor"] = ("Введите # и шесть шестнадцатеричных цифр.", "Enter # followed by six hexadecimal digits."),
        ["Cancel"] = ("Отмена", "Cancel"),
    };
}
