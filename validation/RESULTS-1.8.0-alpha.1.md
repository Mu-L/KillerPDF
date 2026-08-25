# Standards-conformance validation results - KillerPDF 1.8.0-alpha.1

Validation date: 2026-08-25

KillerPDF 1.8.0-alpha.1 resaved the complete 2,907-file conformance corpus through
The KillerPDF.Engine. Among the 2,381 files the engine accepted and wrote, veraPDF found
zero new standards-conformance failures and qpdf found zero structural regressions. Fifty
files became more conformant after serialization.

This report records the first complete corpus run after KillerPDF's document pipeline was
migrated from PdfSharpCore to The KillerPDF.Engine. The historical KillerPDF 1.7.5 results
remain available in [RESULTS.md](RESULTS.md).

## Result

**Pass: zero conformance or structural regressions across every file the engine wrote.**

| Outcome | Files |
|---|---:|
| Corpus total | 2,907 |
| Resaved successfully | 2,381 |
| Rejected while opening | 514 |
| Rejected while saving | 12 |
| Saved with unchanged veraPDF outcome | 2,331 |
| Improved to compliant | 49 |
| Improved by one failed rule | 1 |
| Saved with new veraPDF failures | 0 |
| Saved with worse qpdf status | 0 |

The engine produced 145 more successful resaves than KillerPDF 1.7.5 while preserving the
same zero-regression standard for every output it created.

## Build under test

| Component | Version |
|---|---|
| KillerPDF | 1.8.0-alpha.1 |
| Engine | The KillerPDF.Engine from commit `0c2a000407637628470b70c943bc4492e1b2324f` |
| Runtime | .NET 10, Windows |
| veraPDF | 1.30.2 |
| qpdf | 12.3.2 |

The tested commit is `v1.8.0-alpha.1: complete engine migration`.

## Corpus

The corpus contains 2,907 PDFs from the public veraPDF PDF/A and PDF/UA conformance suites,
the Isartor PDF/A-1b test suite, and TWG test files. These are deliberately hostile inputs.
Many are constructed to violate one exact standards requirement, and some contain malformed
file structures that a parser must reject.

The pristine source corpus was read from `C:\Users\steve\pdf-corpus`. The test did not modify
any source file. Resaved PDFs and reports were written to a separate output directory.

## Method

1. Validate all 2,907 original files recursively with veraPDF and save the JSON baseline.
2. Run KillerPDF's `--batch-resave` command across the complete corpus.
3. Validate all successfully resaved files recursively with the same veraPDF version.
4. Compare the before and after veraPDF results by relative path with
   [Compare-VeraPDF.ps1](Compare-VeraPDF.ps1).
5. Run `qpdf --check` against each of the 2,381 original and resaved file pairs with
   [QpdfSweep.ps1](QpdfSweep.ps1).
6. Treat rejected files separately from saved files. A rejected input has no output that
   could have regressed.

## veraPDF results

veraPDF reported no new failed rule on any of the 2,381 saved files:

| Saved-file outcome | Files |
|---|---:|
| Unchanged | 2,331 |
| Became fully compliant | 49 |
| Failed one fewer rule | 1 |
| Regressed | 0 |

The remaining improvement was the Isartor test-suite manual. Its resaved copy no longer
failed ISO 19005-1:2005 clause 6.1.4 test 3.

The comparison script also reports 526 `MISSING_AFTER` entries because it compares the full
2,907-file baseline with the 2,381-file output tree. Those entries correspond exactly to the
514 open rejections and 12 save rejections. They are not altered PDFs and are excluded from
the saved-file regression count.

## qpdf structural results

`qpdf --check` completed across all 2,381 original and resaved pairs:

| qpdf status before to after | Files |
|---|---:|
| Clean to clean (`0` to `0`) | 2,363 |
| Warning to clean (`3` to `0`) | 12 |
| Warning retained (`3` to `3`) | 5 |
| Error retained (`2` to `2`) | 1 |
| Worsened | 0 |

No saved file became structurally worse. Twelve inputs carrying qpdf warnings were rewritten
as clean files.

## Rejected inputs

The engine rejected 514 files during open because they could not be parsed safely. It rejected
another 12 during save after full-rewrite validation detected invalid source structures:

| Save rejection | Files |
|---|---:|
| Missing or malformed `endstream` syntax | 5 |
| Invalid catalog `/Version` | 3 |
| Non-string trailer `/Info /Title` | 2 |
| Invalid trailer `/Info /ModDate` | 1 |
| Trailer `/Info` did not resolve to a dictionary | 1 |

No rejected input produced an output PDF, and every original remained untouched. This is the
intended safety behavior for sources the engine cannot fully and reliably rewrite.

## Reproduction

Run these commands from the repository root after placing veraPDF and qpdf on `PATH`:

```powershell
verapdf --recurse --format json C:\Users\steve\pdf-corpus > baseline.json

Start-Process -Wait .\bin\Release\net10.0-windows\KillerPDF.exe `
    -ArgumentList '--batch-resave', 'C:\Users\steve\pdf-corpus', `
    'C:\Users\steve\pdf-corpus-resaved', '--log', 'resave.csv'

verapdf --recurse --format json C:\Users\steve\pdf-corpus-resaved > after.json

.\validation\Compare-VeraPDF.ps1 -Baseline baseline.json -After after.json `
    -BaselineRoot C:\Users\steve\pdf-corpus `
    -AfterRoot C:\Users\steve\pdf-corpus-resaved -CsvOut compare.csv

.\validation\QpdfSweep.ps1 -Corpus C:\Users\steve\pdf-corpus `
    -Resaved C:\Users\steve\pdf-corpus-resaved `
    -ResaveLog resave.csv -CsvOut qpdf-results.csv
```

The veraPDF comparison command exits with a failure when outputs are absent, including files
that KillerPDF deliberately rejected. Review `MISSING_AFTER` entries against `resave.csv`, then
evaluate regressions among rows whose output files exist.
