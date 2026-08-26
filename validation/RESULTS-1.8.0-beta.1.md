# KillerPDF 1.8.0-beta.1 validation results

Validation date: 2026-08-25

KillerPDF 1.8.0-beta.1 rewrote 2,898 of 2,907 deliberately hostile conformance PDFs
through The KillerPDF.Engine. That is a 99.7% successful rewrite rate, with zero rewrite
failures, zero saved-file conformance regressions, and zero structural regressions.

On the exact 2,236-file workload supported by KillerPDF 1.7.5, the alpha completed every
rewrite in a median 7.03 seconds instead of 9.46 seconds. That is 25.7% less elapsed time
and 34.6% higher throughput on the test system.

## Headline results

| Result | KillerPDF 1.7.5 | KillerPDF 1.8.0-beta.1 |
|---|---:|---:|
| Complete corpus | 2,907 | 2,907 |
| Successful rewrites | 2,236 | 2,898 |
| Corpus rewrite rate | 76.9% | 99.7% |
| Additional successful rewrites | | **662** |
| Saved files with new veraPDF failures | 0 | **0** |
| Saved files with worse qpdf status | 0 | **0** |

The alpha successfully rewrote 29.6% more files than version 1.7.5, measured against the
1.7.5 accepted set. It retained all 2,236 files that version 1.7.5 could rewrite and added
support for 662 more.

## What changed

The KillerPDF.Engine now performs bounded recovery for common real-world structural defects,
then emits a deterministic full rewrite. This validation round expanded recovery across:

- noncanonical headers and trailing data;
- malformed but recoverable classic cross-reference tables and cross-reference streams;
- inconsistent free-object bookkeeping and linearization offsets;
- recoverable stream opening syntax and incorrect stream lengths;
- malformed optional document information and catalog version declarations;
- signed documents when signature invalidation is explicitly authorized.

For an authorized full rewrite of a signed document, the engine removes stale signature
values and certification permissions while retaining empty signature fields for later
re-signing. It does not present an invalidated signature as though it were still signed.

## Complete corpus result

| Batch outcome | Files |
|---|---:|
| Corpus total | 2,907 |
| Rewritten successfully | **2,898** |
| Intentionally skipped | 9 |
| Rewrite failures | **0** |

The nine skips consist of four encrypted inputs, which batch mode deliberately does not
decrypt or strip, and five parser-hostile files that do not provide enough reliable PDF
structure for a safe rewrite. Every source file remained untouched.

## veraPDF conformance gate

veraPDF compared every successful rewrite with its pristine source by relative path.

| Saved-file outcome | Files |
|---|---:|
| Unchanged conformance result | 2,824 |
| Became fully compliant | **68** |
| Improved by one or more failed rules | **6** |
| Regressed | **0** |

The result is a clean gate: no rewritten file gained a failed veraPDF rule. Seventy-four
outputs improved, including malformed file-structure tests and signed permission tests that
were normalized into honest unsigned rewrites.

The comparison log also records the nine intentional batch skips as `SKIPPED`. They are not
counted as regressions because no altered output exists for them.

## qpdf structural gate

`qpdf --check` compared all 2,898 original and rewritten pairs.

| qpdf status before to after | Files |
|---|---:|
| Clean to clean (`0` to `0`) | 2,511 |
| Warning to clean (`3` to `0`) | **374** |
| Warning retained (`3` to `3`) | 12 |
| Error retained (`2` to `2`) | 1 |
| Worsened | **0** |

No output became structurally worse. The deterministic rewrite removed qpdf warnings from
374 inputs.

## Speed comparison

The benchmark used the shared 2,236-file set successfully handled by KillerPDF 1.7.5. Both
versions processed identical files on the same Windows system. Each version received one
unmeasured warmup, followed by five measured runs in alternating order. The table reports
the median of those five runs.

| Version | Median elapsed | Median throughput |
|---|---:|---:|
| KillerPDF 1.7.5 | 9.46 seconds | 236.48 files/second |
| KillerPDF 1.8.0-beta.1 | **7.03 seconds** | **318.22 files/second** |
| Alpha difference | **25.7% less time** | **34.6% higher throughput** |

This is a batch full-rewrite benchmark, not a claim about every interactive operation or
every computer. The measured alpha speedup on this workload is 1.35 times.

## Build under test

| Component | Version |
|---|---|
| KillerPDF | 1.8.0-beta.1 |
| Engine | The KillerPDF.Engine at commit `4436f3e459cf0429f5fbafd0c416332858390541` |
| Runtime | .NET 10, Windows |
| veraPDF | 1.30.2 |
| qpdf | 12.3.2 |
| Engine tests | 1,424 passed |
| Application tests | 129 passed |
| Release build | 0 warnings, 0 errors |

The historical KillerPDF 1.7.5 report remains unchanged in [RESULTS.md](RESULTS.md).

## Corpus and method

The 2,907-file corpus combines public veraPDF PDF/A and PDF/UA conformance suites, the
Isartor PDF/A-1b suite, and TWG test files. Many inputs are intentionally malformed to
violate one exact standards requirement.

The validation procedure was:

1. Validate the pristine corpus recursively with veraPDF and retain the JSON baseline.
2. Run `KillerPDF.exe --batch-resave` across the complete corpus into a separate tree.
3. Validate every rewritten file with the same veraPDF version.
4. Compare before and after results with [Compare-VeraPDF.ps1](Compare-VeraPDF.ps1), using
   the batch log to distinguish explicit skips from missing outputs.
5. Compare every original and rewritten pair with [QpdfSweep.ps1](QpdfSweep.ps1).
6. Build the intersection accepted by both versions and benchmark it with
   [Benchmark-Versions.ps1](Benchmark-Versions.ps1).

## Reproduction

Run from the repository root after placing veraPDF and qpdf on `PATH`:

```powershell
$Corpus = 'C:\path\to\pdf-corpus'
$Resaved = 'C:\path\to\pdf-corpus-resaved'

verapdf --recurse --format json $Corpus > baseline.json

Start-Process -Wait .\bin\Release\net10.0-windows\KillerPDF.exe `
    -ArgumentList '--batch-resave', $Corpus, $Resaved, '--log', 'resave.csv'

verapdf --recurse --format json $Resaved > after.json

.\validation\Compare-VeraPDF.ps1 -Baseline baseline.json -After after.json `
    -BaselineRoot $Corpus -AfterRoot $Resaved -ResaveLog resave.csv `
    -CsvOut compare.csv

.\validation\QpdfSweep.ps1 -Corpus $Corpus -Resaved $Resaved `
    -ResaveLog resave.csv -CsvOut qpdf-results.csv
```

For the controlled version comparison, create an input directory containing only files
successfully rewritten by both versions, then run:

```powershell
.\validation\Benchmark-Versions.ps1 `
    -BaselineExe 'C:\path\to\KillerPDF-1.7.5.exe' `
    -CandidateExe '.\bin\Release\net10.0-windows\KillerPDF.exe' `
    -InputDirectory 'C:\path\to\shared-input' `
    -OutputDirectory 'C:\path\to\benchmark-output' `
    -Runs 5 `
    -BaselineLabel 'KillerPDF 1.7.5' `
    -CandidateLabel 'KillerPDF 1.8.0-beta.1'
```
