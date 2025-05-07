using System.IO;
using UnityEngine;

public class Testet : MonoBehaviour
{

    [SerializeField] private Texture2D texture;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //first Make sure you're using RGB24 as your texture format

        //then Save To Disk as PNG
        byte[] bytes = texture.EncodeToJPG();
        var dirPath = Application.dataPath + "/../SaveImages/";
        File.WriteAllBytes(dirPath + "Image" + ".jpg", bytes);


        if (!Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }

    }
}
