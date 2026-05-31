ACadSharp.Pdf ![Build&Test](https://github.com/DomCr/ACadSharp.Pdf/actions/workflows/csharp.yml/badge.svg) ![License](https://img.shields.io/github/license/DomCr/ACadSharp.Pdf) ![nuget](https://img.shields.io/nuget/v/Acadsharp.Pdf) [![Coverage Status](https://coveralls.io/repos/github/DomCR/ACadSharp.Pdf/badge.svg?branch=master)](https://coveralls.io/github/DomCR/ACadSharp.Pdf?branch=master)
---

Library to generate Pdf files from dwg and dxf files read by [ACadSharp](https://github.com/DomCR/ACadSharp).

# Features

- Generate Pdf files from a Dwg/Dxf file read by [ACadSharp](https://github.com/DomCR/ACadSharp).
- Generate the preview image for dwg files.

# Beta status

- Missing entities:
    - HATCH: 
    - TEXT | MTEXT | ATT
        - missing fonts, style and multiline
    - DIMENSIONS
    - INSERT
    - UNDERLAY | PDF | IMAGE
    - MLINE
    - MULTILEADER
    - RAY
    - TOLERANCE
    - XLINE