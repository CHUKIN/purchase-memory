# Receipt recognition benchmark

Generated: 2026-07-16T01:05:25.7836910+02:00 on `MacBook-Pro-Dmitriy`.

> This is a small viability benchmark, not a statistically representative accuracy study. Latency includes model loading and therefore reflects the local prototype experience.

| Pipeline | Successful | Mean score | Mean latency | Mean items |
|---|---:|---:|---:|---:|
| apple-vision-translation-foundation | 3/3 | 79.3 | 7.5s | 5.3 |
| hybrid-glm-qwen4 | 2/3 | 92.5 | 12.4s | 2.5 |
| vision-qwen4 | 3/3 | 60.3 | 13.4s | 4.3 |
| vision-qwen9 | 3/3 | 59.3 | 19.6s | 3.7 |
| vision-gemma4-12b | 3/3 | 5.3 | 15.2s | 4.0 |
| vision-minicpm-v4.6 | 3/3 | 45.3 | 7.7s | 5.0 |

## Individual runs

| Pipeline | Receipt | Status | Score | Time | Merchant | Date | Total | Items |
|---|---|---|---:|---:|---|---|---:|---:|
| hybrid-glm-qwen4 | biedronka-2020-01-09-cc-by-sa | success | 85 | 14.1s | Biedronka | 2020-01-09 | 8.27 | 3 |
| hybrid-glm-qwen4 | biedronka-eparagon-2024-07-official | success | 100 | 10.8s | BIEDRONKA | 2024-07-22 | 11.78 | 2 |
| hybrid-glm-qwen4 | lidl-2025-official-sample | failed | — | 206.3s | — | — | — | — |
| vision-qwen4 | biedronka-2020-01-09-cc-by-sa | success | 85 | 23.7s | Biedronka | 2020-01-09 | 8.27 | 3 |
| vision-qwen4 | biedronka-eparagon-2024-07-official | success | 93 | 5.2s | BIEDRONKA "CODZIENNIE NISKIE CENY" 4110 | 2024-07-22 | 11.78 | 2 |
| vision-qwen4 | lidl-2025-official-sample | success | 3 | 11.4s | PARAGON FISKALNY | 2023-10-05 | 62.80 | 8 |
| vision-qwen9 | biedronka-2020-01-09-cc-by-sa | success | 80 | 37.2s | Biedronka | 2020-01-09 | 8.27 | 3 |
| vision-qwen9 | biedronka-eparagon-2024-07-official | success | 78 | 7.5s | Biedronka | 2024-02-27 | 11.78 | 2 |
| vision-qwen9 | lidl-2025-official-sample | success | 20 | 14.1s | PARAGON FISKALNY | 2023-06-15 | 102.62 | 6 |
| vision-gemma4-12b | biedronka-2020-01-09-cc-by-sa | success | 10 | 19.5s | Żabka | 2024-05-13 | 17.99 | 4 |
| vision-gemma4-12b | biedronka-eparagon-2024-07-official | success | 5 | 13.3s | Żabka | 2024-05-13 | 17.99 | 4 |
| vision-gemma4-12b | lidl-2025-official-sample | success | 1 | 12.8s | Żabka | 2024-05-13 | 17.99 | 4 |
| vision-minicpm-v4.6 | biedronka-2020-01-09-cc-by-sa | success | 60 | 14.9s | Sklep 5163 ul. Grabarska 2, 50-079 Wroctaw Jeronimo Martins Polska S.A. | 2020-01-09 | 82700.00 | 3 |
| vision-minicpm-v4.6 | biedronka-eparagon-2024-07-official | success | 55 | 4.0s | BIEDRONKA | — | 11.78 | 5 |
| vision-minicpm-v4.6 | lidl-2025-official-sample | success | 21 | 4.1s | PARAGON FISKALNY | 2024-07-18 | 102.62 | 7 |
| apple-vision-translation-foundation | biedronka-2020-01-09-cc-by-sa | success | 95 | 5.6s | Biedronka | 2020-01-09 | 8.27 | 3 |
| apple-vision-translation-foundation | biedronka-eparagon-2024-07-official | success | 93 | 4.6s | Biedronka | 2024-07-22 | 11.78 | 2 |
| apple-vision-translation-foundation | lidl-2025-official-sample | success | 50 | 12.3s | Lidl | 2021-03-10 | 102.62 | 11 |

The JSON file beside this report contains every extracted line and error message.
