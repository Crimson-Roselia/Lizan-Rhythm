# UI Image 高斯模糊

适用于 Unity 2022.3 / URP 14 的 uGUI Image，Shader 名称为 `Lizan/UI/Gaussian Blur`。

## 使用

1. 将 `Assets/Material/UIGaussianBlur.mat` 拖到 Image 的 **Material** 字段，图片仍在 **Source Image** 中设置。
2. 在材质中调整 **Intensity**（Shader 属性名 `_Intensity`）。`0` 显示原图，数值越大模糊越强，Inspector 范围为 `0–10`，示例材质为 `2`。
3. 图片使用 **Bilinear** 过滤。模糊采样距离以源纹理像素为单位，因此同一数值在不同分辨率图片上的观感会不同。

Shader 使用单 Pass、25 次纹理采样的二维高斯核，不需要 Renderer Feature。支持 Image Color、CanvasGroup 透明度、Mask、RectMask2D 及其 Softness；模糊仍在 Image 已有网格范围内绘制。

## 脚本调整

```csharp
// 多个 Image 需要独立强度时，为各自创建材质实例。
var blurMaterial = Object.Instantiate(image.material);
image.material = blurMaterial;

blurMaterial.SetFloat("_Intensity", Mathf.Max(0f, intensity));
// Mask 会生成 stencil 材质副本，动态调整时也更新实际用于绘制的材质。
image.materialForRendering.SetFloat("_Intensity", Mathf.Max(0f, intensity));
```

应在组件销毁时 `Object.Destroy(blurMaterial)`，避免遗留运行时材质。共享同一材质的 Image 会共享强度。

## Sprite 子区域 / 图集

完整独立纹理保持 **Sprite UV Rect** 为 `(0, 0, 1, 1)`。使用 Sprite Sheet 子图或 Sprite Atlas 时，为每张 Image 使用独立材质，并设置当前 Sprite 的 UV 边界，避免模糊采到相邻图片：

```csharp
var sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
if (sprite != null)
{
    var uvRect = UnityEngine.Sprites.DataUtility.GetOuterUV(sprite);
    blurMaterial.SetVector("_UVRect", uvRect);
    image.materialForRendering.SetVector("_UVRect", uvRect);
}
```

更换 Sprite 或图集完成重新打包后需要重新设置边界。图集应关闭 **Tight Packing**，使采样区域为矩形。
