# Dolly Projector per Windows

Versione Windows di [Dolly Projector](https://www.dollyprojector.app): regia di proiezione per il cinema. mpv a schermo intero sullo schermo della sala, controlli sull'altro schermo. Scritta in C# (.NET 9, WPF); la versione Mac, in Swift, è in [friconelli/dolly-projector](https://github.com/friconelli/dolly-projector).

**Stato: beta.** Il motore è collaudato (da Mac, con i test della versione Mac); l'interfaccia WPF è in prova su PC Windows.

## Struttura
- `src/Dolly.Core` — motore (sequenza, neri, intervalli, lingue/sottotitoli, riavvio di mpv), IPC con mpv (named pipe su Windows, socket Unix altrove), telecomando dal telefono, traduzioni IT/EN.
- `src/Dolly.Cli` — avvio senza finestra con `--test-api PORTA`, per i collaudi automatici.
- `src/Dolly.App` — interfaccia WPF (tema chiaro/scuro di Windows).

## DCP
Come nella versione Mac, Dolly proietta i DCP non cifrati (Aggiungi → *DCP (cartella)…*): dal CPL costruisce una lista di montaggio per mpv con video e audio di tutti i reel e un SRT con i sottotitoli di testo. Dettagli, limiti e collaudo (`tools/make_test_dcp.py`) nel README di [dolly-projector](https://github.com/friconelli/dolly-projector). Il codice è in `src/Dolly.Core/Dcp.cs`.

## Scaletta: avvio senza cartella e selezione multipla
Come nella versione Mac: l'app parte anche senza cartella dei film (scaletta vuota) e nella scaletta si selezionano più elementi (clic, Ctrl+clic, Maiusc+clic, Ctrl+A, Esc; Canc li toglie; il blocco si sposta trascinando la maniglia o dal menu del clic destro; le impostazioni valgono per tutti i selezionati). Dettagli nel README di [dolly-projector](https://github.com/friconelli/dolly-projector). Azioni del motore: `removemany` e `reordermany`.

## Compilare
Serve il .NET SDK 9 (da Mac o Windows; da Mac il progetto WPF si compila grazie a `EnableWindowsTargeting`).

```sh
dotnet publish src/Dolly.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -o "dist/win/Dolly Projector"
```
Poi copiare `mpv.exe` (e `d3dcompiler_43.dll`) in `dist/win/Dolly Projector/mpv/` (build per Windows: https://github.com/shinchiro/mpv-winbuild-cmake) e comprimere la cartella. Il programma cerca mpv in `mpv\mpv.exe` accanto all'eseguibile, poi in `DOLLY_MPV`, poi nel `PATH`.

## Collaudo del motore
```sh
dotnet publish src/Dolly.Cli -c Release -o out/cli
DOLLY_BIN=$PWD/out/cli/Dolly.Cli DOLLY_MPV=/percorso/di/mpv python3 test_dolly.py CARTELLA_FILM_DI_PROVA
```
`test_dolly.py` e i file di prova sono nel repository della versione Mac.

## Licenza
Il pacchetto distribuito include mpv (GPL v2 o successiva) e FFmpeg: sorgenti e build in https://github.com/shinchiro/mpv-winbuild-cmake e https://github.com/mpv-player/mpv. Per il codice di questo repository non è ancora stata scelta una licenza.
