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
            //������Assembly
            asmPSDImporter = Assembly.GetAssembly( typePsdImporter );
            asmU2DCommon = Assembly.Load( "Unity.2D.Common.Editor" );
            asmPsdPlugin = Assembly.Load( "PsdPlugin" );
            //������Type
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
            //������PropertyInfo
            propertyTextureNativeArrayColor32InPsdLayer = typePsdLayer.GetProperty( "texture", BindingFlags.Public | BindingFlags.Instance );
            propertySurfaceInBitmapLayer = typeBitmapLayer.GetProperty( "Surface", BindingFlags.Public | BindingFlags.Instance );
            propertyColor = typeSurface.GetProperty( "color", BindingFlags.Public | BindingFlags.Instance );
            //������FieldInfo
            fieldBitmapLayerInExtractLayer = typePSDExtractLayerData.GetField( "bitmapLayer", BindingFlags.Public | BindingFlags.Instance );
            fieldPsdSizeInt = typePsdImportData.GetField( "m_DocumentSize", BindingFlags.NonPublic | BindingFlags.Instance );
            fieldPsdLayerDatas = typePsdImportData.GetField( "m_PsdLayerData", BindingFlags.NonPublic | BindingFlags.Instance );
            fieldPsdLayerName = typePsdLayerData.GetField( "m_Name", BindingFlags.NonPublic | BindingFlags.Instance );
            fieldUvInt = typeSpriteMetaData.GetField( "uvTransform" );
            fieldExtractLayerDatas = typePsdImporter.GetField( "m_ExtractData", BindingFlags.NonPublic | BindingFlags.Instance );
            fieldImportHiddenLayers = typePsdImporter.GetField( "m_ImportHiddenLayers", BindingFlags.NonPublic | BindingFlags.Instance );
            fieldSpriteMetaDatas = typePsdImporter.GetField( "m_MosaicSpriteImportData", BindingFlags.NonPublic | BindingFlags.Instance );
            //������MethodInfo�����¼������������ڴ˽ű���ר�ŵ��ã���Ϊbase.OnImportAsset���Զ�Dispose�����ǵ����������޷��ڴ˻�ȡ
            var methodsInPSDImporter = typePsdImporter.GetMethods( BindingFlags.NonPublic | BindingFlags.Instance );
            methodSetDocument = methodsInPSDImporter.First( method => method.GetParameters().Length == 1 && method.Name == "SetDocumentImportData" );
            var methodsInImagePacker = typeImagePacker.GetMethods( BindingFlags.Public | BindingFlags.Static );
            methodPack = methodsInImagePacker.First( method => method.GetParameters().Length == 9 && method.Name == "Pack" );
            methodLoad = typePsdLoad.GetMethod( "Load", new[] { typeof( Stream ) } );
            methodExecute = typeExtractLayerTask.GetMethod( "Execute", BindingFlags.Public | BindingFlags.Static );
        }

        public override void OnImportAsset( AssetImportContext ctx ) {
            //NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;//����NativeArrayδdispose����й¶ʱ�򿪴˴����㶨λ
            if ( methodExecute == null ) InitReflectionInfo();
            //�������ñ������� base.OnImportAsset����Ȼ�ᵼ�¶��import
            useCharacterMode = false;
            filterMode = FilterMode.Point;
            mipmapEnabled = false;
            spriteImportMode = SpriteImportMode.Multiple;
            //spritePixelsPerUnit = 32;
            anisoLevel = 0;
            //����������������������������������������������������������������������������������������
            base.OnImportAsset( ctx );

            //����PsdImportData���ԭʼpsd�ĵ��ߴ�
            List<Object> objsInCtx = ctx.GetObjs();
            Object objPsdImportData = objsInCtx.First( item => item.GetType() == typePsdImportData );
            Vector2Int psdSizeInt = (Vector2Int)fieldPsdSizeInt.GetValue( objPsdImportData );
            Vector2 psdSize = psdSizeInt;

            //������psdDoc��SetDocumentImportDataʹm_ExtractDataˢ��texture����
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
            //׼��Unity.2D.Common.Editor.ImagePacker.Pack�����
            object[] parasPack;
            NativeArray<Color32>[] buffers = GetNativeArrayColor32Buffers( extractLayerDatas, importHiddenLayers );
            int padding = 4;
            NativeArray<Color32> outPackedBuffer = default;
            int outPackedBufferWidth = 0, outPackedBufferHeight = 0;
            RectInt[] outPackedRect = default;
            Vector2Int[] outUVTransform = default;
            //׼������
            RectInt[] resultRectInts = default;
            Vector2Int[] resultUvInts = default;
            NativeArray<Color32> resultPackedBuffer = default;
            try {
                int indexOfRectInts = 8;
                int indexOfUvInts = 9;
                int indexOfPackedBuffer = 5;
#if UNITY_2022_2_OR_NEWER //����PSDImporter7��8�Ĳ�������
                parasPack = new object[] { buffers, psdSizeInt.x, psdSizeInt.y, padding, 0u, outPackedBuffer, outPackedBufferWidth, outPackedBufferHeight, outPackedRect, outUVTransform };
#else
                parasPack = new object[] { buffers, psdSizeInt.x, psdSizeInt.y, padding, outPackedBuffer, outPackedBufferWidth, outPackedBufferHeight, outPackedRect, outUVTransform };
                indexOfRectInts--; indexOfUvInts--; indexOfPackedBuffer--;
#endif
                methodPack.Invoke( null, parasPack );
                //��Pack����������
                resultRectInts = (RectInt[])parasPack[indexOfRectInts];
                resultUvInts = (Vector2Int[])parasPack[indexOfUvInts];//ԭʼuvֻ�ܴ������õ�,SpriteMetaData�м�¼������Ҳ�Ǵ���������
                resultPackedBuffer = (NativeArray<Color32>)parasPack[indexOfPackedBuffer];

                //UnityEditor.U2D.PSD��SpriteMetaData�ܱ����޷�ֱ��ʹ�ã�����ʹ���丸��SpriteRect��Ҳ����ֱ��תΪList��ֻ����תΪIenumerable
                List<SpriteRect> SpriteMetaDatas = ( fieldSpriteMetaDatas.GetValue( this ) as IEnumerable<SpriteRect> ).ToList();
                object[] psdLayerDatas = (object[])fieldPsdLayerDatas.GetValue( objPsdImportData );

                //����psd�ײ����ƻ�ȡ�Զ���pivot����
                Vector2 targetPivotInPsd = psdSize * 0.5f;//δ��psd��ָ����Ĭ��ȡpsd���ĵ㣨��λΪ���أ�
                //Vector2 targetPivotInPsd = new( psdSize.x * 0.2f, psdSize.y * 0.8f );//�����޸�Ĭ��pivot��λ
                if ( SpriteMetaDatas != null && SpriteMetaDatas.Count > 0 && psdLayerDatas != null && psdLayerDatas.Length > 0 ) {
                    string firstLayerName = (string)fieldPsdLayerName.GetValue( psdLayerDatas[0] );
                    //����һͼ������pivot��ͷʱ��������psd�Զ���pivotλ�ã���ȡ�˲�rect�����ĵ�λ��Ȼ��ɾ�����
                    if ( firstLayerName.StartsWith( "pivot", StringComparison.InvariantCultureIgnoreCase ) ) {
                        //��λpivot��sprite��һ�ִ���ᱻɾ���������rect��uv�����ݱ���ͨ��pack������ԭʼpsd�����»�ã�����apply��ڶ��ξ�������ʱ���Ҳ������ݡ�����ͼ���е���ָʾȡcenter������ȡposition����ͼ�����½�
                        targetPivotInPsd = resultRectInts[0].center.GetOriginPosInPsd( resultUvInts[0] );//������ԭʼuv����

                        if ( SpriteMetaDatas[0].name.StartsWith( "pivot", StringComparison.InvariantCultureIgnoreCase ) ) {
                            SpriteMetaDatas.RemoveAt( 0 );//������ж�sprite[0]�����name����ɾ��������ᵼ��ÿ��apply��ɾ����0��
                        }
                        else Debug.Log( $"AutoSetPivot to: {targetPivotInPsd / psdSize} (relative to PSD) @ [{assetPath}]" );//ֻ�ڵڶ����ӡ�������ظ���ӡ
                    }
                    else Debug.LogWarning( $"No pivot target in PSD, will use PSD center point instead @ [{assetPath}]" );
                }

                //�޸�ÿһ��sprite��pivot��ע��Ҫ��Apply֮ǰ
                AutoSetNameAndPivotsFromPsd( SpriteMetaDatas, targetPivotInPsd, true );

                var thisISEDP = this as ISpriteEditorDataProvider;
                thisISEDP.SetSpriteRects( SpriteMetaDatas.ToArray() );
                thisISEDP.Apply();//�ֶ�ApplyӦ��Sprite�����Ӧ��ǰ����������ٴν���OnImport����
            }
            finally {
                //NativeArray��Disposeר����ע�ⲻҪ��©������������������������������������������������������������������������������������
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
                //��������������������������������������������������������������������������������������������������������������������������
            }
        }

        public void ApplyManually() {
            var thisISEDP = this as ISpriteEditorDataProvider;
            thisISEDP.Apply();
            AssetDatabase.Refresh();
        }

        /// <summary> ͨ��ExtractLayerTask.Excute��ȡ�������</summary>
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

        /// <summary>��SpriteMetaData(����ʱ�������Ϊ����SpriteRect)��uvTransform�ֶ��л�ȡuv�������޸�SpriteMetaDatas��pivotʹ�䰴psd�е�ָ������(��λΪ����)����</summary>
        List<SpriteRect> AutoSetNameAndPivotsFromPsd( List<SpriteRect> SpriteMetaDatas, Vector2 targetPivotInPsd, bool autoRename ) {
            for ( int i = 0; i < SpriteMetaDatas.Count; i++ ) {
                var sheet = SpriteMetaDatas[i];
                if ( autoRename ) sheet.name = "Frame" + ( i + 1 ).ToString().PadLeft( 4, '0' );
                var uvFloat = (Vector2Int)fieldUvInt.GetValue( sheet );//��SpriteMetaData(����ʱ�����������Ϊ����SpriteRect)��uvTransform�ֶ��л�ȡuv����
                sheet.alignment = SpriteAlignment.Custom;//�޸�sprite��pivotģʽΪ�Զ���
                Vector2 rectPosInPsd = sheet.rect.position.GetOriginPosInPsd( uvFloat );//positionΪrect���½�����
                sheet.pivot = ( targetPivotInPsd - rectPosInPsd ) / sheet.rect.size;//������������=������ӦԪ��֮������Զ��
            }
            //Debug.Log( $"AutoSetPivot: {SpriteMetaDatas.Select( sheet => sheet.name ).ToStrings()} @ {assetPath}" );
            return SpriteMetaDatas;
        }

    }
    [CustomEditor( typeof( PSDImporterFrames ) )]
    internal class PSDImporterFramesEditor : PSDImporterEditor {
    }
}
