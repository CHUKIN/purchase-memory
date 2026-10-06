# Polish receipt test set

Локальный набор публичных изображений для проверки сценария «image input в AI-клиенте → MCP → SQLite». Изображения загружены 15 июля 2026 года только для локального тестирования; бинарные файлы исключены из Git, поскольку права остаются у исходных сайтов.

## Источники

| Файл | Назначение | Источник |
|---|---|---|
| `01-fakturaxl-sample.png` | Отрицательный пример: документ явно помечен как `RACHUNEK NIEFISKALNY`; в базу не сохранялся | [FakturaXL — wzór paragonu](https://www.fakturaxl.pl/paragon) |
| `02-sklep-u-asi.jpg` | Короткий иллюстративный чек: 3 позиции | [Gazeta.pl — пример польского чека](https://kobieta.gazeta.pl/pieniadze/7,197740,32548641,skarbowka-apeluje-nie-daj-sie-oszukac-przy-kasie-wystarczy.html) |
| `03-spolem.webp` | Средний реальный чек: количества и весовой товар, дата обрезана | [Autoblog — paragon ze sklepu osiedlowego](https://autoblog.spidersweb.pl/jazda-samochodem-sie-oplaca-koszt-paliwa-sklep-osiedlowy) |
| `04-wielkopolska.jpg` | Длинный реальный чек: 9 строк, повтор товара, магазин обрезан | [BiznesInfo — zakupy w sklepie osiedlowym](https://www.biznesinfo.pl/zrobilismy-zakupy-w-sklepie-osiedlowym-w-wielkopolsce-paragon-mowi-sam-za-ps-231022) |

## Эталон и фактический результат Codex

| Проверка | Короткий | Społem | Wielkopolska |
|---|---:|---:|---:|
| Видимых товарных строк | 3 | 7 | 9 |
| Правильно сохранено с первого раза | 3 | 6 | 8 |
| Выдуманных строк | 0 | 0 | 0 |
| Магазин | распознан | распознан | корректно `null` |
| Дата | распознана | корректно `null` | распознана |
| Итог | 19,70 PLN | 48,04 PLN | 64,33 PLN |
| `purchaseId` | `b8b7aaa4-a060-4b4a-b00d-3c112e517713` | `142bda48-924d-4747-9cd7-7cbe3d751a70` | `27324c1b-ea2f-4046-9a98-5583602ce7bc` |

Первичная точность названий строк: 17 из 19, или 89,5%. В Społem модель прочитала `SUROMSKA` вместо `SURÓWKA`; отдельный сеанс Codex успешно выполнил `search_purchases → update_purchase_draft`. В длинном чеке осталась одна неисправленная OCR-ошибка: `Masnix` вместо `Masmix`.

Ответы независимых сеансов клиента сохранены в папке `results/`. Серверные вызовы находятся в `logs/purchase-memory-20260715.log`.

## Продолженные сценарии

- `07-validation-error-response.md` — модель получила понятную ошибку пустого `items` и не стала выдумывать товар;
- `08-duplicate-response.md` — зафиксировал исходный дефект: повторная фотография создала второй черновик;
- `09-duplicate-protected-response.md` — после исправления модель получила существующий ID и не обошла защиту;
- `10-arithmetic-warning-response.md` — модель сохранила исходные числа и показала предупреждение о расхождении 9,00 и 10,00.

Защита от дублей сравнивает нормализованный магазин, момент покупки, валюту, итог и все строки. Это разумная эвристика для client-assisted extraction; точный hash изображения станет возможен только когда backend начнёт получать исходный файл.

## Важная оговорка

Это проверка Codex Desktop/CLI с локальным MCP, а не Claude Mobile. Она доказывает работоспособность client-assisted extraction и выбора инструментов, но не заменяет отдельную проверку удобства на телефоне.
