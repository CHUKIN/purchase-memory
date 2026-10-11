import type { HelpGroupId, HelpLinkId, HelpTopicId } from "./topics";

/**
 * Words of the «Подсказки» section, registered in i18n under `help.*`.
 * Every topic describes behaviour that exists in the app today; when a stage
 * changes that behaviour, the matching topic changes with it (see AGENTS.md).
 */
export interface HelpTopicText {
  title: string;
  summary: string;
  steps: string[];
  see: string[];
  trouble: string[];
}

export interface HelpText {
  ui: {
    navLabel: string;
    eyebrow: string;
    title: string;
    intro: string;
    contentsLabel: string;
    skipToTopics: string;
    searchLabel: string;
    searchPlaceholder: string;
    noResults: string;
    resetSearch: string;
    stepsTitle: string;
    seeTitle: string;
    troubleTitle: string;
    linksTitle: string;
    backToContents: string;
    contextLink: string;
    contextLinkFor: string;
    welcomeTitle: string;
    welcomeBody: string;
    welcomeOpen: string;
    welcomeDismiss: string;
  };
  groups: Record<HelpGroupId, string>;
  links: Record<HelpLinkId, string>;
  topics: Record<HelpTopicId, HelpTopicText>;
}

export const helpRu: HelpText = {
  ui: {
    navLabel: "Подсказки",
    eyebrow: "Гид по приложению",
    title: "Подсказки",
    intro: "Как пользоваться историей покупок: от фото чека до вопросов к аналитике. Темы сгруппированы по задачам, в каждой — шаги, что вы увидите и что делать, если что-то пошло не так.",
    contentsLabel: "Содержание подсказок",
    skipToTopics: "Перейти к темам",
    searchLabel: "Найти подсказку",
    searchPlaceholder: "Например: скидка, бренд, фото",
    noResults: "Ничего не нашлось. Попробуйте другое слово.",
    resetSearch: "Показать все темы",
    stepsTitle: "Как это сделать",
    seeTitle: "Что вы увидите",
    troubleTitle: "Если что-то пошло не так",
    linksTitle: "Открыть в приложении",
    backToContents: "К содержанию",
    contextLink: "Как это работает",
    contextLinkFor: "Как это работает: {{topic}}",
    welcomeTitle: "Первый раз здесь?",
    welcomeBody: "В «Подсказках» коротко описано, как добавить чек, проверить его и найти покупку или ответ на вопрос о тратах.",
    welcomeOpen: "Открыть подсказки",
    welcomeDismiss: "Скрыть"
  },
  groups: {
    start: "Начало",
    capture: "Новый чек",
    history: "История покупок",
    catalog: "Товары и впечатления",
    attention: "Проверка данных",
    analytics: "Аналитика",
    advanced: "Для владельца"
  },
  links: {
    home: "Главная",
    capture: "Добавить чек",
    purchases: "История покупок",
    needsAttentionFilter: "Покупки, которые нужно проверить",
    products: "Товары",
    uncategorizedProducts: "Товары без категории",
    reviewQueue: "Нужно проверить",
    analytics: "Аналитика"
  },
  topics: {
    "sign-in": {
      title: "Вход и общий доступ",
      summary: "В приложение входят только участники, которых подтвердил владелец. Все подтверждённые участники видят одни и те же покупки. Войти нужно один раз на каждом устройстве: дальше вход сохраняется неделями.",
      steps: [
        "В браузере: введите адрес почты и нажмите «Прислать ссылку для входа». Откройте ссылку из письма или введите код из него и нажмите «Войти по коду».",
        "В приложении на экране «Домой» iPhone: введите почту, нажмите «Получить код на почту», скопируйте цифры из письма и вставьте их в поле «Код из письма». Ссылку в письме здесь не нажимайте — она откроется в браузере, а не в приложении.",
        "Если владелец включил вход через Google, можно нажать «Продолжить через Google».",
        "При первом входе появится экран «Заявка отправлена». Когда владелец подтвердит доступ, нажмите «Проверить снова».",
        "Язык интерфейса переключается кнопками RU / EN, выйти можно кнопкой «Выйти»."
      ],
      see: [
        "Покупки, товары и аналитика общие для всей семьи.",
        "Впечатления о товарах у каждого свои: в них видно, кто оставил оценку.",
        "В истории изменений чека видно, кто и что поправил."
      ],
      trouble: [
        "«Код не подошёл или устарел» — нажмите «Отправить письмо ещё раз» и введите код из самого нового письма. Один код подходит для одного входа.",
        "«Вход не завершился в этом окне» — ссылка из письма или Google открылись не там, где вы начинали вход. Вернитесь туда и введите код из письма.",
        "«Вход через Google не вернулся в приложение» — войдите по коду из письма: на iPhone это самый надёжный путь.",
        "«Нет связи с сервером» — проверьте интернет и нажмите «Повторить». Если вы уже входили на этом устройстве, вход при этом сохраняется.",
        "«Проверяем вход…» держится долго — сервер просыпается после простоя, это занимает до минуты.",
        "«Доступ заблокирован» — доступ закрыл владелец, свяжитесь с ним.",
        "«Сессия закончилась» — просто войдите снова.",
        "«Слишком много чеков за короткое время» — подряд можно загрузить 10 чеков, дальше один раз в 3 минуты."
      ]
    },
    "iphone-app": {
      title: "Приложение на iPhone",
      summary: "Покупки можно поставить на экран «Домой» iPhone: они откроются как отдельное приложение, без адресной строки. У такого приложения свой вход, отдельный от браузера.",
      steps: [
        "Откройте адрес приложения в Safari или Chrome на iPhone.",
        "Нажмите «Поделиться» (квадрат со стрелкой; в Safari на iOS 26 он спрятан под кнопкой «…»), затем «На экран «Домой»». Если есть переключатель «Открыть как веб-приложение», оставьте его включённым, и нажмите «Добавить».",
        "Откройте приложение значком «Покупки» на экране «Домой».",
        "Войдите внутри приложения по коду из письма: введите почту, нажмите «Получить код на почту» и вставьте цифры из письма. iPhone часто сам предлагает код над клавиатурой.",
        "Дальше приложение помнит вход и продлевает его само, пока вы не нажмёте «Выйти» или не удалите значок."
      ],
      see: [
        "Вход в браузере и вход в приложении на экране «Домой» — разные: если вы вошли в Safari или Chrome, в приложении всё равно нужно войти один раз.",
        "Новая версия приложения подхватывается сама при следующем открытии."
      ],
      trouble: [
        "Снова открылся экран входа — скорее всего, вы входили по ссылке из письма: она открылась в браузере. Войдите в приложении по коду.",
        "Удалили значок с экрана «Домой» — вход в приложении тоже удалится, войдите заново.",
        "«Нет связи с сервером» после долгого перерыва — вход сохранён, проверьте интернет и нажмите «Повторить»."
      ]
    },
    capture: {
      title: "Фото чека",
      summary: "Чек можно снять камерой или выбрать из галереи. Длинный чек снимайте частями — до 8 фото на один чек.",
      steps: [
        "Нажмите «Добавить чек» (на телефоне — «Сфотографировать чек» на главной).",
        "«Сфотографировать» открывает камеру и добавляет одно фото. «Выбрать из галереи» позволяет выбрать сразу несколько.",
        "Если выбрали несколько фото, отметьте «Части одного чека» (так выбрано сразу). Несколько разных чеков — см. тему «Несколько чеков за раз».",
        "Расставьте части по порядку стрелками вверх и вниз: в этом порядке чек будет прочитан. Лишнюю часть удалите корзиной.",
        "Нажмите «Загрузить и распознать»."
      ],
      see: [
        "Предпросмотр каждой части с её номером.",
        "После загрузки открывается страница чека с ходом обработки."
      ],
      trouble: [
        "Подходят только JPEG, PNG и WebP, до 15 МБ на часть и 60 МБ на весь чек.",
        "Приложение пока не обрезает, не выравнивает и не упорядочивает фото само — снимайте чек ровно и при хорошем свете.",
        "«Этот чек уже загружен» — тот же набор фото в том же порядке уже есть. Откройте существующий чек.",
        "Если распознавание недоступно (индикатор в меню), кнопка загрузки не работает. История покупок при этом доступна."
      ]
    },
    "capture-batch": {
      title: "Несколько чеков за раз",
      summary: "Можно выбрать в галерее сразу несколько разных чеков — каждое фото станет отдельным чеком. До 10 фото за раз.",
      steps: [
        "Нажмите «Выбрать из галереи» и отметьте фото чеков — по одному фото на чек.",
        "В блоке «Что на этих фото?» выберите «Отдельные чеки». «Части одного чека» — это один длинный чек, снятый по кускам.",
        "Нажмите «Загрузить N чеков» и не закрывайте экран, пока фото загружаются: они уходят по одному.",
        "Когда загрузка закончится, откройте нужный чек кнопкой «Открыть чек» или нажмите «На главную». Чтобы начать новую пачку, нажмите «Загрузить другие фото»."
      ],
      see: [
        "У каждого фото свой статус: «В очереди», «Загружаем…», «Загружен, распознаётся», «Уже был загружен» или «Не удалось загрузить».",
        "«Не загружено» — загрузка остановилась раньше, чем дошла до этого фото (например, из-за лимита). Такие фото отправит кнопка «Загрузить оставшиеся».",
        "Каждый чек распознаётся отдельно — 10 фото означают 10 распознаваний. Когда чек готов, появляется уведомление, как при обычной загрузке.",
        "Загруженные чеки видны на главной в блоке «Чеки в работе»."
      ],
      trouble: [
        "«Не удалось загрузить» — нажмите «Повторить» у этого фото. Остальные фото это не останавливает.",
        "«Лимит загрузок» — за короткое время загружено слишком много чеков. Загрузка останавливается; когда пройдёт указанное время, нажмите «Загрузить оставшиеся».",
        "«Уже был загружен» — такое же фото уже есть, новый чек не создан. Его можно открыть кнопкой «Открыть чек».",
        "Каждое фото — до 15 МБ, JPEG, PNG или WebP."
      ]
    },
    recognition: {
      title: "Распознавание чека",
      summary: "Распознавание идёт в фоне. С экрана можно уйти — когда чек будет готов, появится уведомление.",
      steps: [
        "Дождитесь статуса «Черновик готов» на странице чека или в блоке «Чеки в работе» на главной.",
        "Нажмите «Открыть чек» в уведомлении или карточку чека на главной, чтобы перейти к проверке."
      ],
      see: [
        "Шаги: «Фотографии загружены» → «Распознаём чек» → «Черновик готов».",
        "Метка «Нужно проверить» на чеке значит, что что-то распознано неуверенно.",
        "Результат никогда не попадает в историю сам: сначала его проверяете вы."
      ],
      trouble: [
        "Статус «Нужна повторная попытка» — фото сохранены. Нажмите «Повторить с сохранёнными фото», когда сервис снова доступен.",
        "Частые неточности: сумма строк не сходится с итогом, не прочитана цена строки, не распознаны магазин или дата. Всё это исправляется перед сохранением.",
        "Нераспознанный чек также появится в разделе «Нужно проверить»."
      ]
    },
    "review-save": {
      title: "Проверка и сохранение",
      summary: "Перед сохранением вы проверяете черновик: магазин, дату, итог и строки. Сомнительные места приложение показывает само.",
      steps: [
        "Если есть неточности, открывается «Нужно уточнить»: карточка на каждую проблему с фрагментом фото чека. Исправьте значение или нажмите «Взять сумму строк».",
        "Если нужно поправить что-то ещё, нажмите «Открыть полный редактор»: там можно менять любые поля, добавлять позиции («Добавить позицию») и удалять лишние кнопкой с корзиной.",
        "Нажмите «Сохранить покупку». После сохранения откроется история покупок."
      ],
      see: [
        "Скидки показаны отдельными строками: к какому товару они относятся или что это скидка на весь чек, и цена товара после скидки.",
        "Счётчик «Осталось N неточностей» и сообщение «Все неточности исправлены», когда можно сохранять."
      ],
      trouble: [
        "Если предупреждения остались, первое нажатие покажет их, а кнопка станет «Всё равно сохранить» — так покупку можно сохранить как есть и поправить позже.",
        "Кнопка сохранения неактивна, пока у всех позиций нет названия.",
        "Не сохранили сразу — чек останется в «Чеки в работе» и в «Нужно проверить» как «Чек ждёт сохранения»."
      ]
    },
    feed: {
      title: "Лента и поиск",
      summary: "Все сохранённые покупки — от новых к старым, на главной и в разделе «Покупки».",
      steps: [
        "В поле «Поиск» введите магазин или название позиции так, как оно напечатано на чеке.",
        "Задайте «Дата с» и «Дата по», чтобы ограничить период.",
        "В «Проверка» выберите «Нужно проверить» или «Проверено».",
        "Нажмите на название магазина в карточке, чтобы открыть покупку. «Показать ещё» догружает следующие покупки."
      ],
      see: [
        "Карточка покупки: магазин, дата, сумма, число позиций и статус проверки.",
        "Фильтры складываются друг с другом; «Сбросить фильтры» убирает всё сразу."
      ],
      trouble: [
        "Когда задан период, покупки без распознанной даты в выборку не попадают.",
        "«Ничего не найдено» — ослабьте фильтры или измените запрос.",
        "Сообщение «Показаны только последние покупки» значит, что поиск просмотрел ограниченное число последних записей."
      ]
    },
    filters: {
      title: "Фильтры по категории, бренду и тегу",
      summary: "В «Покупках» и «Товарах» можно оставить только нужную категорию, бренд или тег.",
      steps: [
        "В «Категория» выберите группу (например, «Молочное и яйца — вся группа») — будет всё из неё, или подкатегорию — только она.",
        "«Без категории» показывает то, что ещё не разложено по категориям.",
        "В «Бренд» выберите подсказку или начните печатать: регистр и польские буквы неважны, «laciate» найдёт «Łaciate».",
        "В «Тег» выберите один из своих тегов. Поле появляется, когда создан хотя бы один тег."
      ],
      see: [
        "В карточке покупки строка «Подходит: …» — позиции, из-за которых чек попал в выборку.",
        "На странице товара есть ссылки «Покупки в этой категории», «Покупки бренда …» и «Покупки с тегом «…»»."
      ],
      trouble: [
        "Фильтры хранятся в адресе страницы: ссылку можно отправить, а после «Назад» выборка сохранится.",
        "Товар без категории не найдётся по фильтру категории — сначала задайте ему категорию.",
        "Фильтры складываются: «Категория» + «Тег» покажет только товары, у которых есть и то и другое. «Сбросить фильтры» в «Истории покупок» и «Сбросить поиск» в «Товарах» убирают всё сразу, включая тег."
      ]
    },
    "purchase-edit": {
      title: "Исправление сохранённой покупки",
      summary: "Сохранённую покупку можно поправить в любой момент — ошибки в истории не навсегда.",
      steps: [
        "Откройте покупку: нажмите на магазин в ленте, «Открыть и исправить покупку» на странице чека или пункт в «Нужно проверить».",
        "Нажмите «Исправить». Меняйте магазин, дату, итог, строки, цены и скидки, добавляйте строки, а лишнюю убирайте кнопкой с корзиной в её правом верхнем углу.",
        "У строки можно выбрать товар: из похожих, через поиск, создать новый или оставить автоматический подбор.",
        "Сохраните правку."
      ],
      see: [
        "Сверху видно, сходятся ли строки с итогом чека и какая разница. Строки без цены подсвечены.",
        "Выбор товара запоминается для этого магазина: в следующий раз такая строка свяжется сама.",
        "Если у покупки есть фото чека, правки попадают в историю изменений чека (кнопка «Показать историю изменений»)."
      ],
      trouble: [
        "Если покупку уже поменяли в другом окне или на другом устройстве, ваша правка не затрёт чужую: приложение предложит загрузить свежую версию.",
        "Пункты «Сумма не сходится», «Нет цены» и «Товар не сопоставлен» из «Нужно проверить» открывают правку сразу на нужном месте и пропадают после сохранения. После «Сохранить исправления» вы вернётесь в «Нужно проверить» с теми же фильтрами."
      ]
    },
    products: {
      title: "Товары",
      summary: "Один товар — одна карточка, даже если магазины печатают его название по-разному. Товары появляются сами, когда вы сохраняете чеки.",
      steps: [
        "Откройте «Товары» и найдите товар по части названия так, как оно напечатано на чеке.",
        "Откройте карточку товара, чтобы увидеть цены и историю покупок."
      ],
      see: [
        "Последняя покупка, цена в прошлый раз, минимальная и средняя цена, как часто покупаем.",
        "График «Как менялась цена» и та же таблица числами. Цены за разные единицы (штука, килограмм) считаются отдельно.",
        "«Названия на чеках» — все варианты написания товара по магазинам."
      ],
      trouble: [
        "Если цену строки прочитать не удалось, она не входит в расчёты — об этом есть заметка.",
        "Сообщение «позиции ждут ручной проверки сопоставления» — строка похожа на товар, но уверенности не хватило. Разберите её в «Нужно проверить»."
      ]
    },
    categories: {
      title: "Категории и бренд",
      summary: "У каждого товара одна категория из двухуровневого дерева (группа → подкатегория) и бренд. Ваше решение всегда главнее подсказки AI.",
      steps: [
        "После сохранения покупки AI в фоне предлагает категорию и бренд новым товарам. Такие значения отмечены «предложено».",
        "В блоке «Категория и бренд» на странице товара нажмите «Подтвердить» или кнопку с карандашом справа от заголовка блока («Изменить»; на телефоне видна только иконка).",
        "Свои категории добавляйте в «Управление категориями» внизу страницы «Товары»; переименовать — кнопка с карандашом, удалить — с корзиной."
      ],
      see: [
        "Исправление применяется ко всем покупкам этого товара — прошлым и будущим.",
        "AI никогда не перезаписывает то, что подтвердил человек.",
        "Фильтр «Категория» есть и в «Нужно проверить»: так можно разобрать, например, только «Молоко» или только товары «Без категории»."
      ],
      trouble: [
        "При удалении подкатегории её товары переходят в группу; при удалении группы — в «Без категории».",
        "Группу, в которой остались подкатегории, удалить нельзя — сначала удалите или перенесите подкатегории.",
        "Два одинаковых названия рядом не допускаются (регистр и пробелы не учитываются)."
      ]
    },
    tags: {
      title: "Теги товаров",
      summary: "Теги — ваши дополнительные срезы: «для ребёнка», «к празднику», «акция», «веган». У товара одна категория, а тегов может быть сколько угодно.",
      steps: [
        "Откройте карточку товара и в блоке «Теги» нажмите кнопку с карандашом справа от заголовка («Изменить теги»; на телефоне видна только иконка).",
        "Нажмите на тег, чтобы добавить или убрать его. Нового тега нет в списке — введите его в «Новый тег» и нажмите «Создать тег»: он сразу отметится.",
        "Нажмите «Сохранить».",
        "Переименовать (карандаш) или удалить (корзина) тег можно в «Управление тегами» внизу страницы «Товары»."
      ],
      see: [
        "Теги видны в списке «Товары» и на карточке товара.",
        "Фильтр «Тег» есть в «Истории покупок», в «Товарах» и в «Нужно проверить»; на карточке товара — ссылка «Покупки с тегом «…»».",
        "В «Аналитике» можно спросить про тег («Сколько потратили на детское?») или выбрать поле «Тег» и разбивку «По тегам».",
        "Теги придумывает только человек: приложение само теги не создаёт и не расставляет."
      ],
      trouble: [
        "Одинаковые теги не создаются: регистр, лишние пробелы и польские буквы не учитываются («Акция» и «акция» — один тег).",
        "Длина тега — до 40 символов, у одного товара — не больше 20 тегов.",
        "Удалённый тег пропадает у всех товаров, а сами товары и покупки остаются."
      ]
    },
    impressions: {
      title: "Впечатления о товарах",
      summary: "Отметьте, понравился ли товар, — необязательно и в любой момент. Потом легко вспомнить, что брать снова, а что нет.",
      steps: [
        "На главной в блоке «Оценить недавнее» нажмите «Не нравится», «Средне» или «Нравится».",
        "На странице покупки у строки нажмите «Оценить» — там можно добавить заметку и фото.",
        "На странице товара в «Впечатления» выберите, какую покупку оцениваете (магазин, дата, цена)."
      ],
      see: [
        "У каждого участника своя история впечатлений. Изменилось мнение — оставьте новое, старое сохранится.",
        "«Сравнение по магазинам» показывает, где товар понравился, а где нет (появляется, когда есть впечатления из двух магазинов)."
      ],
      trouble: [
        "Своё впечатление можно удалить кнопкой с корзиной («Удалить») на его карточке, чужое — нельзя.",
        "Оценённая позиция пропадает из «Оценить недавнее»."
      ]
    },
    "review-queue": {
      title: "Нужно проверить",
      summary: "Очередь того, что стоит поправить или подтвердить, чтобы история и аналитика оставались точными.",
      steps: [
        "Откройте «Нужно проверить» в боковом меню (на телефоне — с главной, блок «Разделы»).",
        "Предложенную категорию подтвердите одной кнопкой «Подтвердить» или выберите другую.",
        "«Товар не сопоставлен»: под строкой чека показаны до трёх похожих товаров из каталога (название, бренд и категория, если они есть). Если это он — нажмите «Да, это он» рядом с ним. Если такого товара ещё нет — «Создать новый товар». Если это другой товар из каталога — «Другой товар» откроет покупку с поиском товара.",
        "Остальное открывается по кнопке — «Открыть чек», «Исправить», «Выбрать товар» — сразу на нужном месте. Если сохранить чек или покупку, открытые отсюда, вы вернётесь в «Нужно проверить» с теми же фильтрами.",
        "Чтобы разобрать только часть, выберите категорию в поле «Категория» (например, «Молоко» или «Без категории») и/или тег в поле «Тег» (например, «Для ребёнка»). «Все категории», «Все теги» или «Сбросить фильтр» возвращают всю очередь. Поле «Тег» есть, когда у вас есть хотя бы один тег."
      ],
      see: [
        "Группы: чек не распознан, чек ждёт сохранения, сумма не сходится, нет цены, товар не сопоставлен, категория не подтверждена, без категории.",
        "«Да, это он» и «Создать новый товар» сохраняют покупку так же, как выбор товара в редакторе покупки: магазин запомнит связь, и в следующий раз такая строка свяжется сама. Пункт сразу исчезает из очереди, счётчик в меню уменьшается.",
        "Счётчик рядом с пунктом меню показывает, сколько пунктов осталось во всей очереди — даже когда на экране выбраны категория или тег.",
        "Категория и тег берутся у товара. С фильтром остаются товары, которые под него подходят (категория не подтверждена, без категории), и покупки, где хотя бы одна строка — такой товар (сумма не сходится, нет цены). Категория-группа включает свои подкатегории; предложенная AI категория тоже считается.",
        "Когда всё разобрано — «Всё проверено»."
      ],
      trouble: [
        "Показаны только первые пункты каждой группы; остальные появятся, когда вы разберёте эти.",
        "Очередь считается по сохранённым данным: пункт исчезает сам, как только причина исправлена.",
        "С выбранной категорией или тегом не видно нераспознанных и несохранённых чеков и несопоставленных строк: у них ещё нет товара, а значит и категории с тегом. Сбросьте фильтр, чтобы увидеть их.",
        "«По этому фильтру всё проверено» значит, что по подходящим товарам вопросов нет; остальная очередь может быть не пустой.",
        "«Эту строку уже разобрали — очередь обновлена»: товар для строки уже выбрали в другом месте (например, в редакторе покупки), поэтому ничего не изменилось.",
        "Похожих товаров нет — тогда на карточке «Выбрать товар» и «Открыть похожий товар», как раньше."
      ]
    },
    analytics: {
      title: "Аналитика: вопросы о покупках",
      summary: "Спросите своими словами — например, «Сколько потратили в Biedronce в августе?». AI только понимает вопрос, а цифры сервер точно считает по сохранённым чекам.",
      steps: [
        "Напишите вопрос или выберите пример и нажмите «Спросить».",
        "Проверьте блок «Как понят вопрос»: чипы показывают товары, категории, теги, магазины и период. Лишний чип уберите крестиком, а в «Изменить» поменяйте что считаем, даты, магазин, тег, разбивку или скидки и нажмите «Пересчитать» — ответ посчитается заново без нового вопроса и без AI.",
        "Чтобы уточнить, задайте следующий вопрос: фильтры прошлого ответа сохранятся. «Новый вопрос» начинает с нуля."
      ],
      see: [
        "Можно спросить про траты, количество, сколько раз покупали, среднюю, минимальную и максимальную цену, покупали ли что-то и на что больше всего трат.",
        "Разбивка по месяцам, неделям, магазинам, категориям, товарам, брендам и тегам.",
        "Теги: если в вопросе есть ваш тег («Сколько потратили на детское?» при теге «Для ребёнка»), ответ посчитается только по товарам с этим тегом — это видно по чипу «#Для ребёнка» и по последней фразе вывода. Поле «Тег» и разбивка «По тегам» появляются, когда в доме есть хотя бы один тег.",
        "В разбивке «По тегам» товар с несколькими тегами считается в каждом из них, поэтому сумма строк может быть больше итога. Товары без тегов собраны в строке «Без тега».",
        "Ни одно число не придумано моделью: AI превращает вопрос в план, а считает и пишет вывод сервер.",
        "В тратах учтены скидки. Скидка на весь чек учитывается, только если вопрос про весь чек, а не про отдельные товары."
      ],
      trouble: [
        "«Похоже, это вопрос не о покупках» — переформулируйте про траты, товары, магазины или цены.",
        "Подряд можно задать до 20 вопросов, дальше — один раз в 3 минуты. Фильтры готового ответа можно менять и после лимита.",
        "Если вопросы выключены на сервере, готовый ответ всё равно можно менять фильтрами.",
        "Ответ неточный — проверьте, не остались ли строки без цены или товары без категории в «Нужно проверить».",
        "«Один из тегов не найден — этот фильтр убран»: тег удалили или такого тега нет, ответ посчитан без него. Если выбрано несколько тегов (через вопрос или MCP), учитываются только товары со всеми ними сразу."
      ]
    },
    "analytics-history": {
      title: "Предыдущие вопросы",
      summary: "Каждый заданный вопрос сохраняется вместе с тем, как он понят, и последним ответом. Повторный вопрос не тратит обращение к AI: сервер берёт сохранённый разбор и считает цифры заново.",
      steps: [
        "Список «Предыдущие вопросы» — под ответом на странице «Аналитика», сверху самые свежие. Нажмите на вопрос — сразу откроется сохранённый ответ.",
        "«Обновить» пересчитывает сохранённый вопрос по сегодняшним чекам, без AI. Период вроде «в этом месяце» или «за прошлый месяц» считается от сегодняшнего дня.",
        "Если задать тот же вопрос ещё раз (регистр и лишние пробелы не важны), ответ посчитается по сохранённому разбору, без AI, а запись в списке обновится, а не задвоится.",
        "«Спросить заново» отправляет вопрос AI ещё раз — пригодится, если вопрос в прошлый раз понят неточно.",
        "Значок корзины рядом с вопросом удаляет его из истории — после подтверждения «Удалить»."
      ],
      see: [
        "История общая для всех участников дома, а не у каждого своя: все видят одни и те же вопросы, любой может открыть, обновить или удалить любой из них — удалённый вопрос пропадает у всех. У каждого вопроса видно, кто задал его последним («вы» или адрес почты).",
        "Уточнения («а по магазинам?») отмечены «Уточнение»: такие слова зависят от прошлого ответа, поэтому повтор уточнения всегда идёт к AI, а «Спросить заново» для них не показывается.",
        "Правка чипов и «Пересчитать» в историю не записываются — сохраняются только заданные вопросы.",
        "Хранятся последние 300 вопросов; в списке «Показать ещё» догружает до 100."
      ],
      trouble: [
        "Повторный вопрос всё равно учитывается в лимите вопросов (20 подряд), хотя AI не вызывается. «Обновить» в лимит вопросов не входит.",
        "Если вопросы выключены на сервере, ранее заданные вопросы всё равно открываются и обновляются; новый вопрос и «Спросить заново» недоступны.",
        "«Не удалось открыть, обновить или удалить сохранённый вопрос» — проверьте связь и попробуйте ещё раз; если вопрос уже удалил другой участник, он просто исчезнет из списка.",
        "«Этот вопрос сохранила старая версия приложения…» — сохранённый разбор вопроса уже не прочитать, поэтому «Обновить» не сработает. Задайте вопрос заново (он запишется поверх) или удалите его."
      ]
    },
    mcp: {
      title: "Доступ для Claude и других AI-клиентов (MCP)",
      summary: "Сервер открывает MCP — способ для AI-ассистента читать историю покупок и задавать вопросы аналитике. Это дополнительный вход; основной — само приложение.",
      steps: [
        "Адрес — /mcp на том же сервере. Доступ есть только у подтверждённых участников, как и у приложения.",
        "Инструменты: поиск покупок и товаров (в том числе по тегу), категории, бренды, теги, очередь «Нужно проверить», впечатления, расчёт аналитики по плану, черновики покупок."
      ],
      see: [
        "Числа в аналитике через MCP считаются так же, как в приложении: сервер проверяет план и сам считает."
      ],
      trouble: [
        "Пока удалённому AI-клиенту нечем получить токен входа, поэтому MCP работает только в локальной разработке. Настройка — задача владельца (см. README)."
      ]
    }
  }
};

export const helpEn: HelpText = {
  ui: {
    navLabel: "Help",
    eyebrow: "App guide",
    title: "Help",
    intro: "How to use Purchase Memory: from a receipt photo to questions about spending. Topics are grouped by task; each has steps, what you'll see, and what to do if something goes wrong.",
    contentsLabel: "Help contents",
    skipToTopics: "Skip to topics",
    searchLabel: "Find a topic",
    searchPlaceholder: "For example: discount, brand, photo",
    noResults: "Nothing found. Try another word.",
    resetSearch: "Show all topics",
    stepsTitle: "How to do it",
    seeTitle: "What you'll see",
    troubleTitle: "If something goes wrong",
    linksTitle: "Open in the app",
    backToContents: "Back to contents",
    contextLink: "How it works",
    contextLinkFor: "How it works: {{topic}}",
    welcomeTitle: "New here?",
    welcomeBody: "Help briefly explains how to add a receipt, check it, and find a purchase or an answer about your spending.",
    welcomeOpen: "Open help",
    welcomeDismiss: "Hide"
  },
  groups: {
    start: "Getting started",
    capture: "New receipt",
    history: "Purchase history",
    catalog: "Products and impressions",
    attention: "Data checks",
    analytics: "Insights",
    advanced: "For the owner"
  },
  links: {
    home: "Home",
    capture: "Add receipt",
    purchases: "Purchase history",
    needsAttentionFilter: "Purchases that need a check",
    products: "Products",
    uncategorizedProducts: "Products without a category",
    reviewQueue: "Needs attention",
    analytics: "Insights"
  },
  topics: {
    "sign-in": {
      title: "Sign-in and shared access",
      summary: "Only members approved by the owner can get in. All approved members see the same purchases. You sign in once per device; after that the sign-in is kept for weeks.",
      steps: [
        "In a browser: enter your email and press “Email me a sign-in link”. Open the link from the email or type the code from it and press “Sign in with code”.",
        "In the app on the iPhone Home Screen: enter your email, press “Email me a sign-in code”, copy the digits from the email and paste them into “Code from the email”. Do not tap the link in the email there: it opens in the browser, not in the app.",
        "If the owner enabled Google sign-in, you can press “Continue with Google”.",
        "On your first sign-in you will see “Request sent”. Once the owner approves you, press “Check again”.",
        "Switch the interface language with RU / EN; leave with “Sign out”."
      ],
      see: [
        "Purchases, products and analytics are shared by the whole household.",
        "Product impressions are personal: each one shows who left it.",
        "A receipt's change history shows who changed what."
      ],
      trouble: [
        "“The code is wrong or expired” — press “Send the email again” and use the code from the newest email. Each code works for one sign-in.",
        "“Sign-in did not finish in this window” — the email link or Google opened somewhere other than where you started. Go back there and enter the code from the email.",
        "“Google sign-in did not come back to the app” — sign in with the code from the email: on iPhone that is the most reliable way.",
        "“No connection to the server” — check the internet and press “Try again”. If you had signed in on this device, you stay signed in.",
        "“Checking sign-in…” takes long — the server is waking up after a pause; it takes up to a minute.",
        "“Access blocked” — the owner closed your access; contact them.",
        "“Your session has ended” — just sign in again.",
        "“Too many receipts in a short time” — you can upload 10 receipts in a row, then one every 3 minutes."
      ]
    },
    "iphone-app": {
      title: "The app on iPhone",
      summary: "You can put Purchase Memory on the iPhone Home Screen: it opens as a separate app without an address bar. That app has its own sign-in, separate from the browser.",
      steps: [
        "Open the app's address in Safari or Chrome on the iPhone.",
        "Tap “Share” (the square with an arrow; in Safari on iOS 26 it is under the “…” button), then “Add to Home Screen”. If there is an “Open as Web App” switch, leave it on, and tap “Add”.",
        "Open the app with the “Покупки” icon on the Home Screen.",
        "Sign in inside the app with the code from the email: enter your email, press “Email me a sign-in code” and paste the digits from the email. iPhone often offers the code above the keyboard.",
        "After that the app remembers you and renews the sign-in itself until you press “Sign out” or remove the icon."
      ],
      see: [
        "Signing in in the browser and in the Home Screen app are separate: even if you signed in in Safari or Chrome, sign in once more inside the app.",
        "A new version of the app is picked up automatically the next time you open it."
      ],
      trouble: [
        "The sign-in screen came back — most likely you used the link from the email, which opened in the browser. Sign in inside the app with the code.",
        "You removed the icon from the Home Screen — its sign-in is removed too; sign in again.",
        "“No connection to the server” after a long break — you are still signed in; check the internet and press “Try again”."
      ]
    },
    capture: {
      title: "Receipt photo",
      summary: "Take a receipt with the camera or pick it from the gallery. Shoot a long receipt in parts — up to 8 photos per receipt.",
      steps: [
        "Press “Add receipt” (on a phone — “Photograph a receipt” on Home).",
        "“Take a photo” opens the camera and adds one photo. “Choose from gallery” lets you pick several at once.",
        "If you picked several photos, keep “Parts of one receipt” selected (it is the default). For several different receipts, see “Several receipts at once”.",
        "Put the parts in order with the up and down arrows: the receipt is read in this order. Remove an extra part with the bin.",
        "Press “Upload and recognize”."
      ],
      see: [
        "A preview of every part with its number.",
        "After upload the receipt page opens and shows the processing progress."
      ],
      trouble: [
        "Only JPEG, PNG and WebP are accepted, up to 15 MB per part and 60 MB per receipt.",
        "The app does not crop, straighten or reorder photos yet — shoot the receipt straight and in good light.",
        "“This receipt is already uploaded” — the same photos in the same order already exist. Open the existing receipt.",
        "If recognition is unavailable (see the indicator in the menu), upload does not work. Purchase history stays available."
      ]
    },
    "capture-batch": {
      title: "Several receipts at once",
      summary: "You can pick several different receipts in the gallery at once — each photo becomes its own receipt. Up to 10 photos at a time.",
      steps: [
        "Press “Choose from gallery” and select the receipt photos — one photo per receipt.",
        "In “What is on these photos?” choose “Separate receipts”. “Parts of one receipt” means one long receipt shot in pieces.",
        "Press “Upload N receipts” and keep the screen open while the photos upload: they go one by one.",
        "When the upload is done, open a receipt with “Open receipt” or press “Go to Home”. To start a new set, press “Upload other photos”."
      ],
      see: [
        "Every photo has its own status: “Waiting”, “Uploading…”, “Uploaded, recognizing”, “Already uploaded” or “Upload failed”.",
        "“Not uploaded” — the upload stopped before it reached this photo (for example, because of the limit). “Upload the rest” sends these photos.",
        "Every receipt is recognized separately — 10 photos mean 10 recognitions. A notification appears when a receipt is ready, just like a normal upload.",
        "Uploaded receipts show up on Home under “Receipts in progress”."
      ],
      trouble: [
        "“Upload failed” — press “Retry” next to that photo. The other photos are not affected.",
        "“Upload limit” — too many receipts in a short time. The upload stops; once the stated time has passed, press “Upload the rest”.",
        "“Already uploaded” — the same photo already exists and no new receipt was created. Open it with “Open receipt”.",
        "Each photo can be up to 15 MB, JPEG, PNG or WebP."
      ]
    },
    recognition: {
      title: "Receipt recognition",
      summary: "Recognition runs in the background. You can leave the screen — a notification appears when the receipt is ready.",
      steps: [
        "Wait for “Draft is ready” on the receipt page or in “Receipts in progress” on Home.",
        "Press “Open receipt” in the notification, or the receipt card on Home, to review it."
      ],
      see: [
        "Steps: “Photos uploaded” → “Recognizing receipt” → “Draft is ready”.",
        "A “Needs attention” label means something was read with low confidence.",
        "A result never goes into history on its own: you check it first."
      ],
      trouble: [
        "“Retry needed” — the photos are kept. Press “Retry with saved photos” when the service is available again.",
        "Common problems: line totals don't match the receipt total, a line price wasn't read, store or date wasn't recognized. All of these are fixed before saving.",
        "A failed receipt also shows up in “Needs attention”."
      ]
    },
    "review-save": {
      title: "Review and save",
      summary: "Before saving you check the draft: store, date, total and lines. The app points out doubtful places itself.",
      steps: [
        "If there are problems, “Needs a closer look” opens: one card per problem with a crop of the receipt photo. Fix the value or press “Use the line sum”.",
        "To change anything else, press “Open full editor”: every field can be edited there, lines added with “Add item” and removed with the bin button.",
        "Press “Save purchase”. Purchase history opens after saving."
      ],
      see: [
        "Discounts are separate lines: which product they belong to, or that they apply to the whole receipt, and the product price after the discount.",
        "A “N issues left” counter and “Every issue is resolved” when you can save."
      ],
      trouble: [
        "If warnings remain, the first press shows them and the button becomes “Save anyway” — so you can save as is and fix it later.",
        "Saving is disabled until every line has a name.",
        "Didn't save right away — the receipt stays in “Receipts in progress” and in “Needs attention” as “Receipt waiting to be saved”."
      ]
    },
    feed: {
      title: "Feed and search",
      summary: "All saved purchases, newest first, on Home and in “Purchases”.",
      steps: [
        "Type a store or a line name into “Search” as it is printed on the receipt.",
        "Set “Date from” and “Date to” to limit the period.",
        "In “Review” choose “Needs attention” or “Reviewed”.",
        "Press the store name on a card to open the purchase. “Show more” loads the next purchases."
      ],
      see: [
        "A purchase card: store, date, total, number of lines and review status.",
        "Filters combine; “Reset filters” clears everything at once."
      ],
      trouble: [
        "Once a period is set, purchases without a recognized date are left out.",
        "“Nothing found” — loosen the filters or change the query.",
        "“Only the most recent purchases are shown” means the search looked through a limited number of recent records."
      ]
    },
    filters: {
      title: "Category, brand and tag filters",
      summary: "In “Purchases” and “Products” you can keep only one category, brand or tag.",
      steps: [
        "In “Category” choose a group (for example “Dairy & eggs — whole group”) to see all of it, or a subcategory to see only that.",
        "“No category” shows what has not been sorted into categories yet.",
        "In “Brand” pick a suggestion or start typing: case and Polish letters don't matter, “laciate” finds “Łaciate”.",
        "In “Tag” choose one of your tags. The field appears once at least one tag exists."
      ],
      see: [
        "A purchase card shows a “Matches: …” line — the lines that brought the receipt into the results.",
        "A product page has “Purchases in this category”, “Purchases of …” and “Purchases tagged “…”” links."
      ],
      trouble: [
        "Filters live in the page address: you can send the link, and “Back” keeps the results.",
        "A product without a category won't match a category filter — give it a category first.",
        "Filters combine: “Category” + “Tag” shows only products that have both. “Reset filters” in “Purchase history” and “Clear search” in “Products” clear everything at once, the tag included."
      ]
    },
    "purchase-edit": {
      title: "Fixing a saved purchase",
      summary: "A saved purchase can be fixed at any time — mistakes in history are not permanent.",
      steps: [
        "Open the purchase: press the store in the feed, “Open and correct the purchase” on the receipt page, or an item in “Needs attention”.",
        "Press “Correct”. Change store, date, total, lines, prices and discounts; add lines, and remove one with the bin button in its top right corner.",
        "For each line you can choose a product: from similar ones, by search, create a new one, or keep automatic matching.",
        "Save the change."
      ],
      see: [
        "The top shows whether the lines add up to the receipt total and by how much they differ. Lines without a price are highlighted.",
        "Your product choice is remembered for that store: next time such a line links by itself.",
        "If the purchase has a receipt photo, changes go into the receipt's change history (“Show change history”)."
      ],
      trouble: [
        "If the purchase was already changed in another window or device, your edit won't overwrite it: the app offers to load the fresh version.",
        "“Total does not match”, “Missing price” and “Product not matched” items in “Needs attention” open the editor right at the spot and disappear after saving. After “Save corrections” you come back to “Needs attention” with the same filters."
      ]
    },
    products: {
      title: "Products",
      summary: "One product — one card, even if stores print its name differently. Products appear on their own as you save receipts.",
      steps: [
        "Open “Products” and search by part of the name as printed on the receipt.",
        "Open a product card to see prices and purchase history."
      ],
      see: [
        "Last purchase, last price, lowest and average price, how often we buy it.",
        "A “How the price moved” chart and the same table in numbers. Prices per different units (piece, kilogram) are counted separately.",
        "“Receipt spellings” — every spelling of the product by store."
      ],
      trouble: [
        "If a line price couldn't be read, it is left out of the numbers — a note says so.",
        "“lines are waiting for their catalog match to be confirmed” — a line looks like a product, but confidence was too low. Sort it out in “Needs attention”."
      ]
    },
    categories: {
      title: "Categories and brand",
      summary: "Each product has one category from a two-level tree (group → subcategory) and a brand. Your decision always wins over the AI suggestion.",
      steps: [
        "After a purchase is saved, AI suggests a category and brand for new products in the background. Such values are marked “suggested”.",
        "In “Category and brand” on the product page press “Confirm” or the pencil button next to the block title (“Change”; on a phone only the icon is shown).",
        "Add your own categories in “Manage categories” at the bottom of “Products”; rename with the pencil button, delete with the bin."
      ],
      see: [
        "A fix applies to every purchase of that product, past and future.",
        "AI never overwrites what a person confirmed.",
        "The “Category” filter is in “Needs attention” too: work through, say, only “Milk” or only “No category” products."
      ],
      trouble: [
        "Deleting a subcategory moves its products to the group; deleting a group moves them to “No category”.",
        "A group that still has subcategories can't be deleted — remove or move the subcategories first.",
        "Two identical names side by side are not allowed (case and spaces are ignored)."
      ]
    },
    tags: {
      title: "Product tags",
      summary: "Tags are your own extra slices: “for the kids”, “for the holidays”, “on sale”, “vegan”. A product has one category but can have any number of tags.",
      steps: [
        "Open a product page and press the pencil button next to the “Tags” title (“Edit tags”; on a phone only the icon is shown).",
        "Tap a tag to add or remove it. If the tag isn't there yet, type it into “New tag” and press “Create tag”: it gets selected right away.",
        "Press “Save”.",
        "Rename (pencil) or delete (bin) tags in “Manage tags” at the bottom of “Products”."
      ],
      see: [
        "Tags are shown in the “Products” list and on the product page.",
        "The “Tag” filter is in “Purchase history”, “Products” and “Needs attention”; the product page has a “Purchases tagged “…”” link.",
        "In “Insights” you can ask about a tag (“How much did we spend on kids' stuff?”) or pick the “Tag” field and the “By tag” breakdown.",
        "Only people create tags: the app never makes up or assigns tags by itself."
      ],
      trouble: [
        "Duplicate tags aren't created: case, extra spaces and Polish letters are ignored (“Sale” and “sale” are one tag).",
        "A tag is up to 40 characters, and one product can have at most 20 tags.",
        "A deleted tag disappears from every product; the products and purchases stay."
      ]
    },
    impressions: {
      title: "Product impressions",
      summary: "Mark whether you liked a product — optional and any time. Later it's easy to remember what to buy again.",
      steps: [
        "On Home, in “Rate recent purchases”, press “Dislike”, “So-so” or “Like”.",
        "On a purchase page press “Rate” on a line — you can add a note and a photo there.",
        "On a product page, in “Impressions”, choose which purchase you're rating (store, date, price)."
      ],
      see: [
        "Each member has their own impression history. Changed your mind — leave a new one; the old one stays.",
        "“Compare by store” shows where the product was liked and where not (it appears once there are impressions from two stores)."
      ],
      trouble: [
        "You can delete your own impression with the bin button (“Delete”) on its card, not someone else's.",
        "A rated line disappears from “Rate recent purchases”."
      ]
    },
    "review-queue": {
      title: "Needs attention",
      summary: "A queue of what's worth fixing or confirming so history and analytics stay accurate.",
      steps: [
        "Open “Needs attention” in the side menu (on a phone — from Home, “Sections”).",
        "Confirm a suggested category with one “Confirm” press, or choose another one.",
        "“Product not matched”: up to three similar catalog products are shown under the receipt line (name, brand and category when known). If it's one of them, press “Yes, this one” next to it. If the product isn't in the catalog yet — “Create a new product”. If it's a different catalog product — “Another product” opens the purchase with the product search.",
        "Everything else opens with a button — “Open receipt”, “Correct”, “Choose product” — right at the spot. When you save a receipt or purchase opened from here, you come back to “Needs attention” with the same filters.",
        "To work through just part of it, pick a category in the “Category” field (for example, “Milk” or “No category”) and/or a tag in the “Tag” field (for example, “For the kids”). “All categories”, “All tags” or “Reset filter” bring back the whole queue. The “Tag” field appears once you have at least one tag."
      ],
      see: [
        "Groups: receipt not recognized, receipt waiting to be saved, total does not match, missing price, product not matched, category not confirmed, no category.",
        "“Yes, this one” and “Create a new product” save the purchase just like picking a product in the purchase editor: the store remembers the link, so next time such a line is matched by itself. The item leaves the queue right away and the menu counter goes down.",
        "The counter next to the menu item shows how many items are left in the whole queue — even while a category or tag is picked on the screen.",
        "Category and tag come from the product. With a filter you see products that match it (category not confirmed, no category) and purchases where at least one line is such a product (total does not match, missing price). A category group includes its subcategories; an AI-suggested category counts too.",
        "When everything is done — “All checked”."
      ],
      trouble: [
        "Only the first items of each group are shown; the rest appear as you work through these.",
        "The queue is computed from saved data: an item disappears as soon as its cause is fixed.",
        "With a category or tag picked, failed or unsaved receipts and unmatched lines are hidden: they have no product yet, so no category or tag either. Reset the filter to see them.",
        "“Nothing to check for this filter” means the matching products are fine; the rest of the queue may still have items.",
        "“This line was already sorted out — the queue is refreshed”: the product for this line was already chosen elsewhere (for example, in the purchase editor), so nothing was changed.",
        "No similar products — then the card shows “Choose product” and “Open similar product”, as before."
      ]
    },
    analytics: {
      title: "Insights: questions about purchases",
      summary: "Ask in your own words — for example, “How much did we spend at Biedronka in August?”. AI only understands the question; the server computes the numbers exactly from saved receipts.",
      steps: [
        "Type a question or pick an example and press “Ask”.",
        "Check “How the question was understood”: the chips show products, categories, tags, stores and period. Remove an extra chip with its cross, or use “Edit” to change what is counted, dates, store, tag, breakdown or discounts and press “Recalculate” — the answer is recomputed without a new question and without AI.",
        "To refine, ask a follow-up: the previous answer's filters are kept. “New question” starts over."
      ],
      see: [
        "You can ask about spending, quantity, how many times you bought something, average, lowest and highest price, whether you bought something, and what you spent the most on.",
        "Breakdown by month, week, store, category, product, brand and tag.",
        "Tags: if the question names one of your tags (“How much did we spend on kids' stuff?” with a “For the kids” tag), only products with that tag are counted — you'll see a “#For the kids” chip and the last sentence of the summary says so. The “Tag” field and the “By tag” breakdown appear once the household has at least one tag.",
        "In the “By tag” breakdown a product with several tags is counted under each of them, so the rows can add up to more than the total. Products without tags are in the “No tag” row.",
        "No number is made up by the model: AI turns the question into a plan, the server counts and writes the summary.",
        "Spending includes discounts. A whole-receipt discount counts only when the question is about whole receipts, not individual products."
      ],
      trouble: [
        "“That does not look like a question about purchases” — rephrase it around spending, products, stores or prices.",
        "Up to 20 questions in a row, then one every 3 minutes. The filters of an existing answer still work after the limit.",
        "If questions are turned off on the server, an existing answer can still be changed with filters.",
        "Answer looks off — check “Needs attention” for lines without a price or products without a category.",
        "“A tag was not found — that filter was removed”: the tag was deleted or doesn't exist, and the answer is computed without it. With several tags (from a question or MCP) only products carrying all of them are counted."
      ]
    },
    "analytics-history": {
      title: "Previous questions",
      summary: "Every question you ask is saved together with how it was understood and its latest answer. Asking it again does not call AI: the server reuses the saved interpretation and recounts the numbers.",
      steps: [
        "The “Previous questions” list is below the answer on the Insights page, newest first. Tap a question to open its saved answer right away.",
        "“Refresh” recounts the saved question on today's receipts, without AI. A period like “this month” or “last month” is counted from today.",
        "Asking the same question again (case and extra spaces don't matter) answers it from the saved interpretation, without AI, and updates the list entry instead of adding a duplicate.",
        "“Ask again” sends the question to AI once more — useful if it was misunderstood last time.",
        "The bin icon next to a question deletes it from the history after you confirm with “Delete”."
      ],
      see: [
        "The history is shared by the whole household, not kept per person: everyone sees the same questions and anyone can open, refresh or delete any of them — a deleted question disappears for everyone. Each question shows who asked it last (“you” or an email address).",
        "Follow-ups (“and by store?”) are marked “Follow-up”: their words depend on the previous answer, so repeating a follow-up always goes to AI and “Ask again” is not offered for them.",
        "Chip edits and “Recalculate” are not saved to the history — only asked questions are.",
        "The last 300 questions are kept; “Show more” loads up to 100 in the list."
      ],
      trouble: [
        "A repeated question still counts towards the question limit (20 in a row) even though AI is not called. “Refresh” does not count towards it.",
        "If questions are turned off on the server, earlier questions still open and refresh; a new question and “Ask again” are unavailable.",
        "“The saved question could not be opened, refreshed or deleted” — check the connection and try again; if another member already deleted it, it simply disappears from the list.",
        "“This question was saved by an older version of the app…” — its saved interpretation can no longer be read, so “Refresh” won't work. Ask the question again (it replaces the old entry) or delete it."
      ]
    },
    mcp: {
      title: "Access for Claude and other AI clients (MCP)",
      summary: "The server exposes MCP — a way for an AI assistant to read purchase history and ask analytics questions. It's an extra entry point; the app itself is the main one.",
      steps: [
        "The address is /mcp on the same server. Only approved members have access, just like the app.",
        "Tools: search purchases and products (by tag too), categories, brands, tags, the “Needs attention” queue, impressions, analytics by plan, purchase drafts."
      ],
      see: [
        "Analytics numbers over MCP are computed the same way as in the app: the server checks the plan and counts itself."
      ],
      trouble: [
        "A remote AI client has no way to get a sign-in token yet, so MCP works only in local development. Setting it up is the owner's job (see README)."
      ]
    }
  }
};
