using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SpriteDicing
{
    /// <summary>
    /// Responsible for building atlas textures from diced textures.
    /// </summary>
    public class AtlasTextureBuilder
    {
        private readonly ITextureSerializer textureSerializer;
        private readonly int unitSize;
        private readonly int padding;
        private readonly float uvInset;
        private readonly bool forceSquare;
        private readonly int atlasSizeLimit;

        public AtlasTextureBuilder (ITextureSerializer textureSerializer, int unitSize, int padding,
            float uvInset, bool forceSquare, int atlasSizeLimit)
        {
            this.textureSerializer = textureSerializer;
            this.unitSize = unitSize;
            this.padding = padding;
            this.uvInset = uvInset;
            this.forceSquare = forceSquare;
            this.atlasSizeLimit = atlasSizeLimit;
        }

        public IReadOnlyList<AtlasTexture> Build (IEnumerable<DicedTexture> dicedTextures)
        {
            var atlases = new List<AtlasTexture>();
            var paddedUnitSize = unitSize + padding * 2;
            var unitsPerAtlasLimit = Mathf.FloorToInt(Mathf.Pow(Mathf.FloorToInt(atlasSizeLimit / (float)paddedUnitSize), 2));
            var texturesToPack = new HashSet<DicedTexture>(dicedTextures);

            while (texturesToPack.Count > 0)
            {
                var atlasTexture = CreateAtlasTexture(atlasSizeLimit, atlasSizeLimit);
                var contentToUV = new Dictionary<Hash128, Rect>();
                var packedTextures = new List<DicedTexture>();
                var yToLastX = new Dictionary<int, int>(); // y-pos of a row in the current atlas to x-pos of the last unit in that row.
                var atlasWidth = Mathf.NextPowerOfTwo(paddedUnitSize);

                while (FindTextureToPack(texturesToPack, contentToUV, unitsPerAtlasLimit) is DicedTexture textureToPack)
                {
                    foreach (var unitToPack in textureToPack.UniqueUnits)
                    {
                        if (contentToUV.ContainsKey(unitToPack.ContentHash)) continue;

                        int posX, posY; // Position of the new unit on the atlas texture.
                        // Find row positions that have enough room for more units until next power of two.
                        var suitableYToLastXList = yToLastX.Where(y => atlasWidth - y.Value >= paddedUnitSize * 2).ToArray();
                        if (suitableYToLastXList.Length == 0) // When no suitable rows found.
                        {
                            // Handle corner case when we just started.
                            if (yToLastX.Count == 0)
                            {
                                yToLastX.Add(0, 0);
                                posX = 0;
                                posY = 0;
                            }
                            // Determine whether we need to add a new row or increase x limit.
                            else if (atlasWidth > yToLastX.Last().Key)
                            {
                                var newRowYPos = yToLastX.Last().Key + paddedUnitSize;
                                yToLastX.Add(newRowYPos, 0);
                                posX = 0;
                                posY = newRowYPos;
                            }
                            else
                            {
                                atlasWidth = Mathf.NextPowerOfTwo(atlasWidth + 1);
                                posX = yToLastX.First().Value + paddedUnitSize;
                                posY = 0;
                                yToLastX[0] = posX;
                            }
                        }
                        else // When suitable rows found.
                        {
                            // Find one with the least number of elements and use it.
                            var suitableYToLastX = suitableYToLastXList.OrderBy(y => y.Value).First();
                            posX = suitableYToLastX.Value + paddedUnitSize;
                            posY = suitableYToLastX.Key;
                            yToLastX[posY] = posX;
                        }

                        // Write colors of the unit to the current atlas texture.
                        atlasTexture.SetPixels(posX, posY, paddedUnitSize, paddedUnitSize, unitToPack.PaddedPixels);
                        // Evaluate and assign UVs of the unit to the other units in the group.
                        var unitUVRect = new Rect(posX, posY, paddedUnitSize, paddedUnitSize).Crop(-padding).Scale(1f / atlasSizeLimit);
                        if (uvInset > 0) unitUVRect = unitUVRect.Crop(-uvInset * (unitUVRect.width / 2f));
                        contentToUV.Add(unitToPack.ContentHash, unitUVRect);
                    }

                    texturesToPack.Remove(textureToPack);
                    packedTextures.Add(textureToPack);
                }

                if (packedTextures.Count == 0) throw new Exception("Unable to fit diced textures. Consider increasing atlas size limit.");

                // Crop unused atlas texture space.
                var needToCrop = atlasWidth < atlasSizeLimit || (!forceSquare && yToLastX.Last().Key + paddedUnitSize < atlasSizeLimit);
                if (needToCrop)
                {
                    var croppedHeight = forceSquare ? atlasWidth : yToLastX.Last().Key + paddedUnitSize;
                    var croppedPixels = atlasTexture.GetPixels(0, 0, atlasWidth, croppedHeight);
                    atlasTexture = CreateAtlasTexture(atlasWidth, croppedHeight);
                    atlasTexture.SetPixels(croppedPixels);

                    // Correct UV rects after crop.
                    foreach (var kv in contentToUV.ToArray())
                        contentToUV[kv.Key] = kv.Value.Scale(new Vector2(atlasSizeLimit / (float)atlasWidth, atlasSizeLimit / (float)croppedHeight));
                }

                atlasTexture.alphaIsTransparency = true;
                atlasTexture.Apply();
                var textureAsset = textureSerializer.Serialize(atlasTexture);
                atlases.Add(new AtlasTexture(textureAsset, contentToUV, packedTextures));
            }

            return atlases;
        }

        private static Texture2D CreateAtlasTexture (int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private static DicedTexture FindTextureToPack (IEnumerable<DicedTexture> textures, IDictionary<Hash128, Rect> contentToUV, int unitsPerAtlasLimit)
        {
            foreach (var texture in textures)
                if (CountUnitsToPack(texture, contentToUV) <= unitsPerAtlasLimit)
                    return texture;
            return null;
        }

        private static int CountUnitsToPack (DicedTexture texture, IDictionary<Hash128, Rect> contentToUV)
        {
            return contentToUV.Keys.Count + texture.UniqueUnits.Count(u => !contentToUV.ContainsKey(u.ContentHash));
        }
    }
}
