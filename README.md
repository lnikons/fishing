# Рибалка

Проста 2D-гра-симулятор рибалки на C# (Windows Forms). Гравець закидає вудку, вчасно підсікає рибу, ловить її в мінігрі зі смугою, відкриває види в журналі, продає улов і купує нові локації та покращення.

Команда **$win$**: Никонюк Лев (тімлід, тестування), Бичковський Тимур (інтерфейс), Руденко Арсен (ігрова логіка).

## Структура

| Папка | Вміст | Відповідальний |
| --- | --- | --- |
| `Fishing.Core/` | ігрова логіка, модель даних, збереження | Арсен |
| `Fishing.App/` | форми Windows Forms, мінігра, графіка | Тимур |
| `docs/` | правила гри та параметри риб | Лев |

## Початок роботи

1. Прийняти запрошення до репозиторію (лист на пошті або сторінка github.com/lnikons/fishing/invitations).
2. Встановити [Git](https://git-scm.com/download/win) і Visual Studio 2022.
3. Один раз налаштувати ім'я та пошту (як на GitHub):
   ```
   git config --global user.name "Ім'я Прізвище"
   git config --global user.email "пошта@example.com"
   ```
4. Клонувати репозиторій і перейти на `develop`:
   ```
   git clone https://github.com/lnikons/fishing.git
   cd fishing
   git switch develop
   ```

## Запуск

1. Встановити Visual Studio 2022 з компонентом **«.NET desktop development»**.
2. Відкрити `Fishing.sln`.
3. Зробити `Fishing.App` стартовим проєктом і запустити (F5).

## Робота з репозиторієм

Правила гілок, комітів і pull request-ів — у [CONTRIBUTING.md](CONTRIBUTING.md). Задачі ведуться на дошці KanbanFlow «$win$».
