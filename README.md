# Loggepector

Сборка для Windows x64 из корня проекта:

- `build.bat` или `build.bat aot` — Native AOT, Release, trimming и оптимизация размера. Результат: `artifacts/native-aot/`.
- `build.bat singlefile` — сжатый self-contained exe без Native AOT. Результат: `artifacts/singlefile/`. Нативные библиотеки включены в exe и распаковываются при запуске.

Нужен .NET 8 SDK или новее. Для Native AOT дополнительно нужны Visual Studio Build Tools с компонентами Desktop development with C++ и Windows SDK.

Native AOT с текущими пакетами Avalonia/SkiaSharp оставляет нативные DLL рядом с exe; их нельзя удалять. Профиль singlefile предназначен для распространения одним exe. Карты, инструменты рендера и настройки создаются отдельно при использовании приложения и в публикацию не включаются.

В MapView кнопка «Включить и скачать» сохраняет согласие на загрузку инструментов и запускает подготовку карт в фоне. Статус подготовки отображается в том же окне.
