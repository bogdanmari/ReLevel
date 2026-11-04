using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ReLevel.Revit.UI.Prototype;

internal sealed class ModeHelpWindow : Window
{
    public ModeHelpWindow(Window owner, PrototypeText text, string mode)
    {
        Owner = owner; Icon = AppIcon.Create(); Title = $"ReLevel · {text[mode]} · {text["Help"]}";
        Width = 660; Height = 550; MinWidth = 440; MinHeight = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        var document = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(24, 50, 71)), PagePadding = new Thickness(28),
            ColumnWidth = double.PositiveInfinity };
        document.Blocks.Add(new Paragraph(new Run(text[mode])) { FontSize = 24, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(21, 125, 136)), Margin = new Thickness(0, 0, 0, 8) });
        document.Blocks.Add(new Paragraph(new Run(text[mode + "Description"])) { Margin = new Thickness(0, 0, 0, 18) });
        foreach (var section in Sections(mode, text.English))
        {
            document.Blocks.Add(new Paragraph(new Bold(new Run(section.Title))) { Margin = new Thickness(0, 12, 0, 6) });
            var list = new System.Windows.Documents.List { MarkerStyle = TextMarkerStyle.Disc, Padding = new Thickness(20, 0, 0, 0) };
            foreach (var item in section.Items) list.ListItems.Add(new ListItem(new Paragraph(new Run(item)) { Margin = new Thickness(0, 0, 0, 8) }));
            document.Blocks.Add(list);
        }
        Content = new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }

    private sealed record Section(string Title, params string[] Items);
    private static IEnumerable<Section> Sections(string mode, bool en) => mode switch
    {
        "Compare" => en ? [
            new("Load a reference", "Load a UTF-8 TSV with two columns: level name and elevation relative to the project origin in the units selected in Settings. An optional Name / Elevation header is supported. Decimal points and commas are accepted."),
            new("Read the results", "Names are matched without case sensitivity or leading/trailing spaces. Elevations match within 0.001 mm. Missing levels, different elevations and duplicate names are shown explicitly.", "Green-turquoise rows in Levels indicate matching names and elevations. Duplicate or ambiguous names are not highlighted. Loading or clearing a file does not modify the model; Clear comparison removes the highlights.") ] : [
            new("Загрузите список", "Нужен TSV UTF-8: имя уровня и отметка относительно начала проекта в единицах Settings, разделённые табуляцией. Заголовок Name / Elevation необязателен. Поддерживаются десятичная точка и запятая."),
            new("Сравните результат", "Имена сопоставляются без учёта регистра и пробелов по краям. Допуск отметки — 0,001 мм. Отсутствующие уровни, разные отметки и повторяющиеся имена отмечаются отдельно.", "Зелёно-бирюзовые строки Levels означают совпадение имени и отметки. Повторяющиеся и неоднозначные имена не подсвечиваются. Загрузка и очистка не меняют модель; очистка снимает подсветку.") ],
        "Levels" => en ? [
            new("Transfer from several levels", "Check the source levels and click Transfer Elements. Only checked visible levels are included, using the same base associations as the Elements counts. Choose one target or Each element to the nearest level below. Each object is processed once; upper associations to a different source level remain unchanged. Protect applies only to Distribute Elements."),
            new("Action menu", "Action opens upward with Select Elements, Delete Elements, Delete Views and Delete Level. Delete Level requires exactly one checked visible level and closes the window after the operation."),
            new("Distribute elements", "Check selects retained target levels. Protect preserves their own bindings while allowing incoming elements. Distribute Elements uses all checked levels, including those hidden by search, and assigns bindings to the nearest retained level below, or the lowest if none is below."),
            new("Explore a level", "Click Elements or Views beside a level. The bottom-left back arrow returns to the level overview and preserves its search, sorting and checks.",
                "Elements counts existing supported cases at their base level, including pinned and read-only objects. Views counts non-template views associated with that level. An asterisk marks incomplete analysis."),
            new("Choose objects and a destination", "Check the rows, then click Transfer elements to choose a target or Each element to the nearest level below. Automatic selection uses all project levels, including the source, and only processes bindings to the opened level. In Views, click Recreate views to choose the target level and name prefix in a dialog. Search and Filter limit the visible rows; operations use checked visible rows. Ctrl/Shift selects multiple rows for checking.",
                "Include upper-level associations also shows elements whose upper binding refers to this level. This can make the list larger than the overview count. Unavailable rows explain their restriction."),
            new("Run an action", "Transfer elements changes their level using the existing supported cases. Recreate views asks for a name prefix and creates supported plans on the target level; original views remain.",
                "Select elements highlights checked objects in Revit. Delete elements / Delete views previews dependencies and asks for confirmation. Open views opens checked views. At the overview, selection and deletion apply to all objects on checked visible levels."),
            new("Results and cancellation", "Review the result report: a completed transaction does not prove that every dependency was preserved. Use a copy of the model for validation.",
                "Cancel rolls back the current transfer or recreation run at the next boundary between objects. Earlier completed runs remain. Revit Undo is available after completion; closing the window does not undo changes.") ] : [
            new("Перенос с нескольких уровней", "Отметьте исходные уровни и нажмите Transfer Elements. Учитываются отмеченные видимые уровни по нижней привязке, как в счётчиках Elements. Выберите общую цель или ближайший уровень снизу. Каждый элемент обрабатывается один раз; верхняя привязка к другому исходному уровню сохраняется. Защита действует только для Distribute Elements."),
            new("Меню действий", "Action раскрывается вверх: Select Elements, Delete Elements, Delete Views и Delete Level. Удаление уровня доступно при одном отмеченном видимом уровне и закрывает окно после операции."),
            new("Распределите элементы", "Check выбирает сохраняемые целевые уровни. Защита сохраняет собственные привязки, разрешая принимать элементы. Distribute Elements учитывает все отметки, включая скрытые поиском, и назначает ближайший сохраняемый уровень снизу; если ниже нет — самый нижний."),
            new("Откройте уровень", "Нажмите число Elements или Views. Стрелка назад внизу слева возвращает таблицу уровней с сохранением поиска, сортировки и отметок.",
                "Elements считает объекты существующих кейсов по нижней привязке, включая закреплённые и readonly. Views считает нешаблонные виды связанного уровня. Звёздочка означает неполный анализ."),
            new("Выберите объекты и цель", "Отметьте строки и нажмите «Перенести элементы», чтобы выбрать цель или «Каждый элемент к ближайшему уровню снизу». Автовыбор использует все уровни проекта, включая исходный, и только привязки к открытому уровню. В Views нажмите «Пересоздать виды», чтобы выбрать целевой уровень и префикс имени в диалоге. Поиск и фильтр ограничивают видимые строки; операции используют отмеченные видимые строки. Ctrl/Shift позволяет выделить несколько строк для общей отметки.",
                "«Включить верхние привязки» добавляет объекты с верхом на этом уровне: строк может стать больше, чем в общем счётчике. У недоступных строк указана причина ограничения."),
            new("Выполните действие", "Перенос элементов использует существующие кейсы. Пересоздание видов запрашивает префикс имени и создаёт поддерживаемые планы на целевом уровне; исходные виды остаются.",
                "Выделение показывает отмеченные объекты в Revit. Удаление элементов или видов сначала показывает зависимости и требует подтверждения. Открытие видов открывает отмеченные виды. В общей таблице выделение и удаление относятся ко всем объектам отмеченных видимых уровней."),
            new("Результат и отмена", "Проверьте отчёт: завершение транзакции не доказывает сохранность всех зависимостей. Проверяйте операции на копии модели.",
                "«Отмена» откатывает текущий запуск переноса или пересоздания на границе между объектами. Предыдущие запуски сохраняются. После завершения используйте Undo Revit; закрытие окна не отменяет изменения.") ],
        "Distribute" => en ? [
            new("Choose retained levels", "Check the levels to keep. Protect prevents their own elements from being reassigned, while still allowing incoming elements.", "At least one level must be retained. Search does not limit the operation: all level choices are used."),
            new("Distribute", "Elements go to the nearest retained level below their elevation, or the lowest retained level if none is below. Base and top bindings are assigned separately using existing case restrictions.", "Levels and views are not deleted. Review the report; Cancel rolls back the current run. Use Revit Undo after completion.") ] : [
            new("Выберите сохраняемые уровни", "Отметьте уровни, которые нужно оставить. Защита сохраняет собственные привязки уровня, но разрешает принимать другие элементы.", "Нужен хотя бы один сохраняемый уровень. Поиск не ограничивает операцию: учитываются все отметки уровней."),
            new("Распределите элементы", "Цель — ближайший сохраняемый уровень снизу по высоте; ниже всех уровней — самый нижний. Нижняя и верхняя привязки получают цели отдельно с ограничениями существующих кейсов.", "Уровни и виды не удаляются. Проверьте отчёт. «Отмена» откатывает текущий запуск; после завершения доступен Undo Revit.") ],
        "Colors" => en ? [
            new("Color or hide", "Check levels, pick colors or enable Hide, then click Colorize. All checked levels are processed, including those hidden by the search filter.", "The name count covers the model; In view covers eligible objects in the active view. Multi-level elements belong to their base level."),
            new("Reset and view changes", "Reset restores the graphics captured before the first application for this view and clears level checks. Unchecking a level and applying restores its own graphics.", "Each active view has its own saved state during this window session. Closing keeps changes; use Revit Undo. Unsupported views disable the action buttons and display a reason.") ] : [
            new("Раскрасьте или скройте", "Отметьте уровни, задайте цвет или скрытие и нажмите «Раскрасить». Обрабатываются все отметки, включая скрытые поисковым фильтром.", "Счётчик у имени относится ко всей модели, «На виде» — к подходящим объектам активного вида. Двухуровневые объекты относятся к нижней привязке."),
            new("Сброс и смена вида", "«Сбросить» возвращает графику до первого применения на текущем виде и снимает отметки. Снятие отдельного уровня и повторное применение восстанавливает только его графику.", "Для каждого активного вида хранится своё исходное состояние до закрытия окна. После закрытия используйте Undo Revit. На неподдерживаемом виде кнопки отключены с пояснением.") ],
        "Debug" => en ? [
            new("Diagnostic tools", "Select all elements by… searches the entire model for a chosen case, independently of the active view.", "Add by ID adds objects to the shared analysis using the level last opened in Levels.", "Inspector closes the workspace, asks you to pick an element and appends diagnostics to ELEMENTS.md. It does not change the model.") ] : [
            new("Диагностические инструменты", "«Выделить все элементы по…» ищет выбранный кейс во всей модели независимо от активного вида.", "«Добавить по ID» использует последний открытый уровень в Levels и добавляет объекты в общий анализ.", "Инспектор закрывает окно, предлагает выбрать элемент и дописывает диагностику в ELEMENTS.md. Модель не изменяется.") ],
        _ => en ? [new("Language and navigation", "Choose Project units or Internal — decimal feet for elevation display and TSV import. Already imported lengths keep their physical value. Choose English or Русский. Interface labels change immediately; model names and Revit messages keep their original language.", "Hover over the sidebar to expand it. The menu collapses when the pointer leaves. Page state is retained until the window closes.")] :
            [new("Язык и навигация", "Выберите единицы проекта или встроенные десятичные футы для отметок и импорта TSV. Уже загруженные отметки сохраняют физическую величину. Выберите English или Русский. Подписи меняются сразу; имена модели и сообщения Revit сохраняют исходный язык.", "Наведите курсор на боковую панель для раскрытия; при уходе она сворачивается. Состояние страниц сохраняется до закрытия окна.")]
    };
}
