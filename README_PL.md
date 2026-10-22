# XISF Thumbnails — Windows Explorer (v0.3.4)

Darmowe miniaturki `.xisf` z automatycznym stretchem podobnym do PixInsight STF. Osobne rozszerzenie powłoki Windows; nie zależy od QuickLook i nie zmienia sposobu otwierania plików w PixInsight.

## Instalacja (Windows 10/11 x64)

1. Zainstaluj darmowy [.NET SDK 8 lub nowszy](https://dotnet.microsoft.com/download). ZSTD jest pobierane przy kompilacji przez NuGet.
2. Rozpakuj projekt, otwórz w katalogu **Windows PowerShell 5.1 (64-bit)** bez uprawnień administratora.
3. Uruchom po kolei:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Build.ps1
   powershell -ExecutionPolicy Bypass -File .\Test.ps1
   powershell -ExecutionPolicy Bypass -File .\Install.ps1
   ```

4. Po teście powstanie `TestThumbnail.png`. W Eksploratorze wybierz **Bardzo duże ikony** i sprawdź XISF.
5. Jeśli nadal widzisz ikony: uruchom ponownie Eksplorator i spróbuj na kopii XISF pod nową nazwą. Sprawdź ustawienie „Zawsze pokazuj ikony, nigdy miniatury”.

## Co naprawiono względem poprzednich wersji

- v0.3.1: konflikt `STATSTG` przy kompilacji.
- v0.3.2: ustalanie ścieżki w `Test.ps1`.
- v0.3.3: test wywołuje COM z C#, a nie za pomocą niejednoznacznego mechanizmu przekazywania parametrów PowerShell.
- v0.3.4: **jednolita instalacja** rejestrująca również aktywny ProgID `.xisf` (np. `Pleiades Astrophoto.PixInsight.xisf`). Bez tego poprzednia instalacja mogła działać wyłącznie w teście, ale nie w Eksploratorze.

## Odinstalowanie i aktualizacje

```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall.ps1
```

Przywraca poprzednie skojarzenia miniatur w profilu użytkownika. Jeśli działa już u Ciebie wersja **v0.3.1 z poprawką v0.3.4**, nie musisz niczego zmieniać. Do aktualizacji najpierw odinstaluj stare rozszerzenie; nowy Uninstall rozpoznaje również starszą kopię zapasową po hotfiksie. Nigdy nie instaluj nowej wersji bezpośrednio na starą.

Log dekodera: `%LOCALAPPDATA%\AstroXisfThumbnail\errors.log`. Do dodatkowej diagnostyki służy `tools/Diagnose-XISF-Thumbnails.ps1` (można podać `-XisfFile`). Skrypty starej poprawki zachowano w `tools/`.

## Status

Potwierdzono w Windows 10 działanie zestawu **DLL v0.3.1 + Test v0.3.3 + poprawka skojarzeń v0.3.4**. Scalony instalator v0.3.4 nie został jeszcze osobno sprawdzony na świeżym systemie. To społecznościowy prototyp rozszerzenia systemowego. Dane XISF pozostają niezmienione; AutoSTF nie jest identyczny z PixInsight.

Licencja: MIT ([LICENSE.txt](LICENSE.txt)). Drugi projekt: [QuickLook XISF Viewer](https://github.com/ImixVega/quicklook-xisf-viewer).
