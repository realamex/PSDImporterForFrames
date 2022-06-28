# PSDImporterForFrames
Hack PSDImporter with reflections

若需要将photoshop文件视为帧动画处理——在psd或psb文件的inspector中选择PSDImporterFrames（可以多选修改）

PSDImporterFrames：
若源文件的第一图层名以pivot开头，则会将其视为pivot指示图层，取其图案的中心点去自动修改所有生成出来的sprite的pivot，此图层不会生成sprite。
这个图案的尺寸形状不重要，但建议使用半透明反色中间带亮色的圆形或十字线（参考附带的PSD），并尽可能小，因为指示图层也会占用texture的空间。

若源文件第一图层不以pivot开头，则视为所有图层都是帧图层，并以源文件中心点为参考点生成pivot
可以通过修改Vector2 targetPivotInPsd = psdSize * 0.5f这行改变其默认位置

兼容性测试：2022.1(psdimporter7.0)，2022.2(psdimporter8.0)，更早版本不支持，更新版本不保证

English:
If you need to treat the photoshop file as a frame animation clip - select PSDImporterFrames in the inspector of the psd or psb files (you can still modify them when multiple selecting)

If the first layer name of the source file starts with "pivot", it will be treated as a pivot indicator layer, taking the centre point of its pattern to automatically modify the pivot of all generated sprites, this layer will not generate its sprite.
The size and shape of this pattern is not important, but it is recommended to use a circle or crosshair with a translucent reverse colour with a bright centre (see the accompanying PSDs) and to keep it as small as possible, because the indicator layer will also take up space in the texture.

If the first layer of the source file does not start with a pivot, all layers are considered to be frame layers and the pivot is generated using the centre of the source file as a reference point
The default position can be changed by modifying the line : Vector2 targetPivotInPsd = psdSize * 0.5f

Compatibility: 2022.1 (psdimporter7.0), 2022.2(psdimporter8.0)，earlier versions not supported, newer versions not guaranteed.
