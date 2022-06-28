using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;

using UnityEditor.AssetImporters;
using UnityEditor.U2D.Sprites;
using UnityEditor.U2D.Common;
using System.Reflection;
using Unity.Collections;
using Object = UnityEngine.Object;
using System.IO;
using System.Text;

namespace UnityEditor.U2D.PSD {
    [ScriptedImporter( 100, new string[0], new[] { "psd", "psb" } )]
    internal class PSDImporterFrames : PSDImporter {
        static Assembly asmPSDImporter;
        static Assembly asmU2DCommon;
        static Assembly asmPsdPlugin;

        static Type typePsdImportData;
        static Type typePsdLayerData;
        static Type typeSpriteMetaData;
        static Type typePsdLayer;
        static Type typeExtractLayerTask;
        static Type typePSDExtractLayerData;
        static Type typeImagePacker;
        static Type typePsdLoad;
        static Type typeBitmapLayer;
        static Type typeSurface;

        static PropertyInfo propertyTextureNativeArrayColor32InPsdLayer;
        static PropertyInfo propertySurfaceInBitmapLayer;
        static PropertyInfo propertyColor;

        static FieldInfo fieldBitmapLayerInExtractLayer;
        static FieldInfo fieldPsdSizeInt;
        static FieldInfo fieldPsdLayerDatas;
        static FieldInfo fieldPsdLayerName;
        static FieldInfo fieldUvInt;
        static FieldInfo fieldExtractLayerDatas;
        static FieldInfo fieldImportHiddenLayers;
        static FieldInfo fieldSpriteMetaDatas;

        static MethodInfo methodSetDocument;
        static MethodInfo methodPack;
        static MethodInfo methodLoad;
        static MethodInfo methodExecute;

        static void InitReflectionInfo() {
            var typePsdImporter = typeof( PSDImporter );
            //反射获得Assembly
            asmPSDImporter = Assembly.GetAssembly( typePsdImporter );
            asmU2DCommon = Assembly.Load( "Unity.2D.Common.Editor" );
            asmPsdPlugin = Assembly.Load( "PsdPlugin" );
            //反射获得Type
            typePsdImportData = asmPSDImporter.GetType( "UnityEditor.U2D.PSD.PSDImportData" );
            typePsdLayerData = asmPSDImporter.GetType( "UnityEditor.U2D.PSD.PSDLayerData" );
            typeSpriteMetaData = asmPSDImporter.GetType( "UnityEditor.U2D.PSD.SpriteMetaData" );
            typePsdLayer = asmPSDImporter.GetType( "UnityEditor.U2D.PSD.PSDLayer" );
            typeExtractLayerTask = asmPSDImporter.GetType( "UnityEditor.U2D.PSD.ExtractLayerTask" );
            typePSDExtractLayerData = asmPSDImporter.GetType( "UnityEditor.U2D.PSD.PSDExtractLayerData" );
            typeImagePacker = asmU2DCommon.GetType( "UnityEditor.U2D.Common.ImagePacker" );
            typePsdLoad = asmPsdPlugin.GetType( "PaintDotNet.Data.PhotoshopFileType.PsdLoad" );
            typeBitmapLayer = asmPsdPlugin.GetType( "PDNWrapper.BitmapLayer" );
            typeSurface = asmPsdPlugin.GetType( "PDNWrapper.Surface" );
            //反射获得PropertyInfo
            propertyTextureNativeArrayColor32InPsdLayer = typePsdLayer.GetProperty( "texture", BindingFlags.Public | BindingFlags.Instance );
            propertySurfaceInBitmapLayer = typeBitmapLayer.GetProperty( "Surface", BindingFlags.Public | BindingFlags.Instance );
            propertyColor = typeSurface.GetProperty( "color", BindingFlags.Public | BindingFlags.Instance );
            //反射获得FieldInfo
            fieldBitmapLayerInExtractLayer = typePSDExtractLayerData.GetField( "bitmapLayer", BindingFlags.Public | BindingFlags.Instance );
            fieldPsdSizeInt = typePsdImportData.GetField( "m_DocumentSize", BindingFlags.NonPublic | BindingFlags.Instance );
            fieldPsdLayerDatas = typePsdImportData.GetField( "m_PsdLayerData", BindingFlags.NonPublic | BindingFlags.Instance );
            fieldPsdLayerName = typePsdLayerData.GetField( "m_Name", BindingFlags.NonPublic | BindingFlags.Instance );
            fieldUvInt = typeSpriteMetaData.GetField( "uvTransform" );
            fieldExtractLayerDatas = typePsdImporter.GetField( "m_ExtractData", BindingFlags.NonPublic | BindingFlags.Instance );
            fieldImportHiddenLayers = typePsdImporter.GetField( "m_ImportHiddenLayers", BindingFlags.NonPublic | BindingFlags.Instance );
            fieldSpriteMetaDatas = typePsdImporter.GetField( "m_MosaicSpriteImportData", BindingFlags.NonPublic | BindingFlags.Instance );
            //反射获得MethodInfo，以下几个方法必须在此脚本中专门调用，因为base.OnImportAsset会自动Dispose掉他们的输出结果，无法在此获取
            var methodsInPSDImporter = typePsdImporter.GetMethods( BindingFlags.NonPublic | BindingFlags.Instance );
            methodSetDocument = methodsInPSDImporter.First( method => method.GetParameters().Length == 1 && method.Name == "SetDocumentImportData" );
            var methodsInImagePacker = typeImagePacker.GetMethods( BindingFlags.Public | BindingFlags.Static );
            methodPack = methodsInImagePacker.First( method => method.GetParameters().Length == 9 && method.Name == "Pack" );
            methodLoad = typePsdLoad.GetMethod( "Load", new[] { typeof( Stream ) } );
            methodExecute = typeExtractLayerTask.GetMethod( "Execute", BindingFlags.Public | BindingFlags.Static );
        }

        public override void OnImportAsset( AssetImportContext ctx ) {
            //NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;//碰到NativeArray未dispose导致泄露时打开此处方便定位
            if ( methodExecute == null ) InitReflectionInfo();
            //以下设置必须先于 base.OnImportAsset，不然会导致多次import
            useCharacterMode = false;
            filterMode = FilterMode.Point;
            mipmapEnabled = false;
            spriteImportMode = SpriteImportMode.Multiple;
            //spritePixelsPerUnit = 32;
            anisoLevel = 0;
            //————————————————————————————————————————————
            base.OnImportAsset( ctx );

            //反射PsdImportData获得原始psd文档尺寸
            List<Object> objsInCtx = ctx.GetObjs();
            Object objPsdImportData = objsInCtx.First( item => item.GetType() == typePsdImportData );
            Vector2Int psdSizeInt = (Vector2Int)fieldPsdSizeInt.GetValue( objPsdImportData );
            Vector2 psdSize = psdSizeInt;

            //反射获得psdDoc并SetDocumentImportData使m_ExtractData刷新texture数据
            FileStream fileStream = new( assetPath, FileMode.Open, FileAccess.Read );
            try {
                var objPsdDoc = methodLoad.Invoke( null, new[] { fileStream } );
                methodSetDocument.Invoke( this, new object[] { objPsdDoc } );
            }
            finally {
                fileStream.Close();
            }

            object[] extractLayerDatas = (object[])fieldExtractLayerDatas.GetValue( this );
            bool importHiddenLayers = (bool)fieldImportHiddenLayers.GetValue( this );
            //准备Unity.2D.Common.Editor.ImagePacker.Pack的入参
            object[] parasPack;
            NativeArray<Color32>[] buffers = GetNativeArrayColor32Buffers( extractLayerDatas, importHiddenLayers );
            int padding = 4;
            NativeArray<Color32> outPackedBuffer = default;
            int outPackedBufferWidth = 0, outPackedBufferHeight = 0;
            RectInt[] outPackedRect = default;
            Vector2Int[] outUVTransform = default;
            //准备出参
            RectInt[] resultRectInts = default;
            Vector2Int[] resultUvInts = default;
            NativeArray<Color32> resultPackedBuffer = default;
            try {
                int indexOfRectInts = 8;
                int indexOfUvInts = 9;
                int indexOfPackedBuffer = 5;
#if UNITY_2022_2_OR_NEWER //兼容PSDImporter7与8的参数区别
                parasPack = new object[] { buffers, psdSizeInt.x, psdSizeInt.y, padding, 0u, outPackedBuffer, outPackedBufferWidth, outPackedBufferHeight, outPackedRect, outUVTransform };
#else
                parasPack = new object[] { buffers, psdSizeInt.x, psdSizeInt.y, padding, outPackedBuffer, outPackedBufferWidth, outPackedBufferHeight, outPackedRect, outUVTransform };
                indexOfRectInts--; indexOfUvInts--; indexOfPackedBuffer--;
#endif
                methodPack.Invoke( null, parasPack );
                //对Pack输出结果命名
                resultRectInts = (RectInt[])parasPack[indexOfRectInts];
                resultUvInts = (Vector2Int[])parasPack[indexOfUvInts];//原始uv只能从这里拿到,SpriteMetaData中记录的数据也是从这里来的
                resultPackedBuffer = (NativeArray<Color32>)parasPack[indexOfPackedBuffer];

                //UnityEditor.U2D.PSD的SpriteMetaData受保护无法直接使用，所以使用其父类SpriteRect。也不能直接转为List，只能先转为Ienumerable
                List<SpriteRect> SpriteMetaDatas = ( fieldSpriteMetaDatas.GetValue( this ) as IEnumerable<SpriteRect> ).ToList();
                object[] psdLayerDatas = (object[])fieldPsdLayerDatas.GetValue( objPsdImportData );

                //根据psd首层名称获取自定义pivot坐标
                Vector2 targetPivotInPsd = psdSize * 0.5f;//未在psd中指定则默认取psd中心点（单位为像素）
                //Vector2 targetPivotInPsd = new( psdSize.x * 0.2f, psdSize.y * 0.8f );//方便修改默认pivot定位
                if ( SpriteMetaDatas != null && SpriteMetaDatas.Count > 0 && psdLayerDatas != null && psdLayerDatas.Length > 0 ) {
                    string firstLayerName = (string)fieldPsdLayerName.GetValue( psdLayerDatas[0] );
                    //当第一图层名由pivot开头时，视作由psd自定义pivot位置，提取此层rect的中心点位置然后删掉这层
                    if ( firstLayerName.StartsWith( "pivot", StringComparison.InvariantCultureIgnoreCase ) ) {
                        //定位pivot的sprite第一轮处理会被删掉所以相关rect和uv的数据必须通过pack方法从原始psd中重新获得，否则apply后第二次经过这里时会找不到数据。若用图形中点作指示取center，否则取position代表图形左下角
                        targetPivotInPsd = resultRectInts[0].center.GetOriginPosInPsd( resultUvInts[0] );//必须是原始uv数据

                        if ( SpriteMetaDatas[0].name.StartsWith( "pivot", StringComparison.InvariantCultureIgnoreCase ) ) {
                            SpriteMetaDatas.RemoveAt( 0 );//需额外判断sprite[0]本身的name再做删除，否则会导致每次apply都删除第0层
                        }
                        else Debug.Log( $"AutoSetPivot to: {targetPivotInPsd / psdSize} (relative to PSD) @ [{assetPath}]" );//只在第二遍打印，避免重复打印
                    }
                    else Debug.LogWarning( $"No pivot target in PSD, will use PSD center point instead @ [{assetPath}]" );
                }

                //修改每一个sprite的pivot，注意要在Apply之前
                AutoSetNameAndPivotsFromPsd( SpriteMetaDatas, targetPivotInPsd, true );

                var thisISEDP = this as ISpriteEditorDataProvider;
                thisISEDP.SetSpriteRects( SpriteMetaDatas.ToArray() );
                thisISEDP.Apply();//手动Apply应用Sprite，如果应用前后有区别会再次进入OnImport流程
            }
            finally {
                //NativeArray的Dispose专区，注意不要遗漏——————————————————————————————————————————
                foreach ( var layer in extractLayerDatas ) {
                    var bitmapLayer = fieldBitmapLayerInExtractLayer.GetValue( layer );
                    var surface = propertySurfaceInBitmapLayer.GetValue( bitmapLayer );
                    var color = (NativeArray<Color32>)propertyColor.GetValue( surface );
                    if ( color.IsCreated ) color.Dispose();
                }
                foreach ( var item in buffers ) {
                    if ( item.IsCreated ) item.Dispose();
                }
                if ( resultPackedBuffer.IsCreated ) resultPackedBuffer.Dispose();
                //—————————————————————————————————————————————————————————————
            }
        }

        public void ApplyManually() {
            var thisISEDP = this as ISpriteEditorDataProvider;
            thisISEDP.Apply();
            AssetDatabase.Refresh();
        }

        /// <summary> 通过ExtractLayerTask.Excute获取相关数据</summary>
        NativeArray<Color32>[] GetNativeArrayColor32Buffers( object extractLayerDatas, bool ImportHiddenLayers ) {
            var outPsdLayers = typePsdLayer.ReflectionCreatList();
            methodExecute.Invoke( null, new object[] { outPsdLayers, extractLayerDatas, ImportHiddenLayers } );
            var nativeArrayBufferList = new List<NativeArray<Color32>>();
            foreach ( var layer in outPsdLayers as IEnumerable ) {
                var nativeArrayColor32 = propertyTextureNativeArrayColor32InPsdLayer.GetValue( layer );
                var nativeArrayColor32Array = (NativeArray<Color32>)nativeArrayColor32;
                nativeArrayBufferList.Add( nativeArrayColor32Array );
            }
            var nativeArrayBuffer = nativeArrayBufferList.ToArray();
            return nativeArrayBuffer;
        }

        /// <summary>从SpriteMetaData(因访问保护体现为父类SpriteRect)的uvTransform字段中获取uv参数并修改SpriteMetaDatas的pivot使其按psd中的指定坐标(单位为像素)对齐</summary>
        List<SpriteRect> AutoSetNameAndPivotsFromPsd( List<SpriteRect> SpriteMetaDatas, Vector2 targetPivotInPsd, bool autoRename ) {
            for ( int i = 0; i < SpriteMetaDatas.Count; i++ ) {
                var sheet = SpriteMetaDatas[i];
                if ( autoRename ) sheet.name = "Frame" + ( i + 1 ).ToString().PadLeft( 4, '0' );
                var uvFloat = (Vector2Int)fieldUvInt.GetValue( sheet );//从SpriteMetaData(因访问保护问题体现为父类SpriteRect)的uvTransform字段中获取uv参数
                sheet.alignment = SpriteAlignment.Custom;//修改sprite的pivot模式为自定义
                Vector2 rectPosInPsd = sheet.rect.position.GetOriginPosInPsd( uvFloat );//position为rect左下角坐标
                sheet.pivot = ( targetPivotInPsd - rectPosInPsd ) / sheet.rect.size;//向量四则运算=各个对应元素之间四则远算
            }
            //Debug.Log( $"AutoSetPivot: {SpriteMetaDatas.Select( sheet => sheet.name ).ToStrings()} @ {assetPath}" );
            return SpriteMetaDatas;
        }

    }
    [CustomEditor( typeof( PSDImporterFrames ) )]
    internal class PSDImporterFramesEditor : PSDImporterEditor {
    }
}
