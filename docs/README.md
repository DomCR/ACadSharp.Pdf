# ACadSharp

ACadSharp.Pdf is a pure C# library to write cad files like into pdf.

It also have some complementary capabilities like creating the preview image for a cad file.

## Quick Start

### Create a Pdf file

### Create a thumbnail for dwg file

To store a file with a preview image, thumbnail, to visaulize the content in you can set the property `DwgWriter.Preview` with the information needed.

```C#
string path =  "path to store the dwg document";
CadDocument doc = new CadDocument();    //Your document

DwgPreview preview = doc.CreatePreview();

using (var wr = new DwgWriter(path, doc))
{
    wr.Preview = preview;
    wr.Write();
}
```