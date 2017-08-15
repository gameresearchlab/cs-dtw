using UnityEngine;
using System.Collections;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

public class ShaderManager : MonoBehaviour
{
    static private string[] FileNames;
    static private string[] TempNames;

    //Number of Templates. This and the number of threads needs to be the same to run.
    static private int numOfTemplates = 1024;

    //Length of template.
    static private int motionTemplateLength;

    //Length of Query/Input
    static private int motionQueryLength;

    //Number of bodyparts
    static private int numOfBodyParts = 1;

    static private int minindex = 0;

    static private string[] allGestures = new string[0];
    static private string[] input = new string[0];
    static private string[] gestures;

//========================================================Compute shaders stuff=============================================================
    static public int kiCalc;                           

    static public ComputeBuffer costMatrix;         
    static public ComputeBuffer globalCostMatrix;			
    static public ComputeBuffer motionTemplate;          
    static public ComputeBuffer motionQuery;   
    static public ComputeShader _shader;				

    static float[] costMatrixArray = new float[numOfBodyParts * numOfTemplates * motionTemplateLength * motionQueryLength];
    static float[] globalCostMatrixArray = new float[numOfBodyParts * numOfTemplates * motionTemplateLength * motionQueryLength];

    //dynamic template length
    static ComputeBuffer dynamicTemplate;
    static int[] dynamicTemplateArray = new int[numOfBodyParts * numOfTemplates + 1];

    //sum array is used for indexing in the costmatrix
    static ComputeBuffer sum;
    static int[] sumArray = new int[numOfBodyParts * numOfTemplates + 1];

    static public ComputeBuffer shaderMin;
    static float[] shaderMinArray = new float[numOfTemplates * numOfBodyParts * 2];

    static public ComputeBuffer allMin;
    static int[] allMinArray = new int[numOfTemplates * numOfBodyParts];
//==========================================================================================================================================


    static bool gestureGo = false;
    string[] tb;
    public List<Quaternion> inputGesture = new List<Quaternion>();
    static int currentInputIndex;
    bool start = true;
    bool done = true;
    bool newGes = false;

    static int[] inputArrayCount = new int[numOfBodyParts * numOfTemplates + 1];
    static int[] inputArraySum = new int[numOfBodyParts * numOfTemplates + 1];
    static int[] bodypartsmin = new int[numOfBodyParts];
    static int[] mincounter = new int[numOfTemplates];
    static int stopindex = 100000;
    static int templatecounter = 0;
    static int jumpin = 0;
    static double time;

    static Quaternion[] motionTemplateArray = new Quaternion[numOfBodyParts * numOfTemplates * motionTemplateLength];

    static Quaternion[] motionQueryArray = new Quaternion[numOfBodyParts * motionQueryLength];

    void Start()
    {                                       
        dynamicTemplateArray[0] = 0;
        sumArray[0] = 0;
        inputArrayCount[0] = 0;
        inputArraySum[0] = 0;
    }


    static void compute()
    {
        _shader = Resources.Load<ComputeShader>("csDTW");           // here we link computer shader code file to the shader class
        kiCalc = _shader.FindKernel("dtwCalc");                      // we retrieve kernel index by name from the code

        double ttime = Time.time;

        _shader.SetBuffer(kiCalc, "costMatrix", costMatrix);

        _shader.SetInt("numOfTemp", numOfTemplates);
        _shader.SetInt("tempLen", motionTemplateLength);
        _shader.SetInt("queryLength", motionQueryLength);
        _shader.SetInt("numOfBodyParts", numOfBodyParts);


        _shader.SetBuffer(kiCalc, "shaderMin", shaderMin);
        _shader.SetBuffer(kiCalc, "allMin", allMin);
        _shader.SetBuffer(kiCalc, "dynamicTemplate", dynamicTemplate);
        _shader.SetBuffer(kiCalc, "sum", sum);

        _shader.SetBuffer(kiCalc, "motionQuery", motionQuery);
        _shader.SetBuffer(kiCalc, "motionTemplate", motionTemplate);
        _shader.SetBuffer(kiCalc, "globalCostMatrix", globalCostMatrix);


        _shader.Dispatch(kiCalc, 1024, 1, 1);


        globalCostMatrix.GetData(globalCostMatrixArray);
        costMatrix.GetData(costMatrixArray);


        shaderMin.GetData(shaderMinArray);
        allMin.GetData(allMinArray);

    }

    //find matched template
    void recognize()
    {
        float min = 100000000;
        for (int i = 0; i < numOfBodyParts * numOfTemplates * 2; i += 2)
        {
            if (min > shaderMinArray[1 + i])
            {
                min = shaderMinArray[1 + i];
                minindex = (int)shaderMinArray[i];
            }
        }

        int counter = 1;

        int tindex = (minindex - motionQueryLength - 1) / motionQueryLength;


        if (tindex <= sumArray[counter])
        {
            Debug.Log("Matched template is: " + (int)(Math.Ceiling((double)counter / 6)));

        }

        while (tindex > sumArray[counter])
        {
            
            if ((tindex) < sumArray[counter + 1])
            {
                Debug.Log("Matched template is: " + (int)(Math.Ceiling((double)counter / 6)));

                break;
            }
            counter++;
        }
    }

    //read files from folder and put data into template array
    void gesturefiles()
    {
        var FileNames = Directory.GetFiles(Application.dataPath + "/Template", "*.csv");

        int newsize = allGestures.Length;
        string[] tb;

        if (FileNames == null)
        {
            Debug.Log("Didn't find any files");
            return;
        } // if

        int tcounter = 0;
        int oldsize = 0; ;
        foreach (string str in FileNames)
        {
            Debug.Log(str);
            gestures = File.ReadAllLines(str);
            if(tcounter == 1)
                oldsize = gestures.Length -1;
            for (int i = 0; i < 1024; i++)
            {
                    dynamicTemplateArray[i + 1] = 1200;
                    sumArray[i + 1] = sumArray[i] + dynamicTemplateArray[i + 1];
            }

            Array.Resize<string>(ref allGestures, newsize + gestures.Length);

            for (int i = 0; i < gestures.Length; i++)
            {
                allGestures[tcounter*oldsize + i] = gestures[i];
            }
            tcounter++;
            newsize = allGestures.Length;

        } // foreach

        motionTemplateArray = new Quaternion[allGestures.Length];

        for (int i = 0; i < allGestures.Length; i++)
        {
            tb = allGestures[i].Split(',');

            Quaternion q = new Quaternion(float.Parse(tb[1]), float.Parse(tb[2]), float.Parse(tb[3]), float.Parse(tb[4]));
            motionTemplateArray[i] = q;

        }

        motionTemplate = new ComputeBuffer(motionTemplateArray.Length, 4*4);
        motionTemplate.SetData(motionTemplateArray);


        shaderMin = new ComputeBuffer(shaderMinArray.Length, 4);
        allMin = new ComputeBuffer(allMinArray.Length, 4);


        dynamicTemplate = new ComputeBuffer(dynamicTemplateArray.Length, 4);
        sum = new ComputeBuffer(sumArray.Length, 4);
        dynamicTemplate.SetData(dynamicTemplateArray);
        sum.SetData(sumArray);

    }

    //read files from folder and fill query array
    void getInput()
    {

        FileNames = Directory.GetFiles(Application.dataPath + "/InputGestures", "*.csv");

        if (FileNames == null)
        {
            Debug.Log("Didn't find any files");
            return;
        } // if

        int newsize = input.Length;
        int tcounter = 0;

        gestures = File.ReadAllLines(FileNames[templatecounter]);

        inputArrayCount[tcounter+1] = gestures.Length;
        inputArraySum[tcounter + 1] = inputArraySum[tcounter] + gestures.Length;
        Array.Resize<string>(ref input, newsize + gestures.Length);

        for (int i = 0; i < gestures.Length; i++)
        {
            input[inputArraySum[tcounter] + i] = gestures[i];
        }

        tcounter++;
        newsize = input.Length;

        motionQueryArray = new Quaternion[numOfBodyParts * motionQueryLength];
        costMatrixArray = new float[allGestures.Length * motionQueryLength];
        globalCostMatrixArray = new float[allGestures.Length * motionQueryLength];

        Array.Clear(motionQueryArray, 0, motionQueryArray.Length);
        for (int i = 0; i < motionQueryLength; i++)
        {
            tb = input[i].Split(',');

            Quaternion q = new Quaternion(float.Parse(tb[1]), float.Parse(tb[2]), float.Parse(tb[3]), float.Parse(tb[4]));
            inputGesture.Add(q);
            motionQueryArray[i] = q;
        }

        motionQuery = new ComputeBuffer(motionQueryArray.Length, 4*4);
        motionQuery.SetData(motionQueryArray);


        globalCostMatrix = new ComputeBuffer(globalCostMatrixArray.Length, 4);
        globalCostMatrix.SetData(globalCostMatrixArray);

        costMatrix = new ComputeBuffer(costMatrixArray.Length, 4);
        costMatrix.SetData(costMatrixArray);

        templatecounter++;

    }

    //does the same as getInput() without initialiation of buffers
    void updateinput()
    {
        gestures = File.ReadAllLines(FileNames[templatecounter]);
        inputGesture.Clear();
        for (int i = 0; i < gestures.Length; i++)
        {
            input[i] = gestures[i];
        }

        Array.Clear(motionQueryArray, 0, motionQueryArray.Length);
        for (int i = 0; i < motionQueryLength; i++)
        {
            tb = input[i].Split(',');
            Quaternion q = new Quaternion(float.Parse(tb[1]), float.Parse(tb[2]), float.Parse(tb[3]), float.Parse(tb[4]));
            inputGesture.Add(q);
            motionQueryArray[i] = q;
        }

        motionQuery.SetData(motionQueryArray);
        templatecounter++;
    }

    void Update()
    {
        if (done)
        {
            time = (new TimeSpan(DateTime.Now.Ticks)).TotalMilliseconds;
            if (templatecounter == 0)
                motionQueryLength = 30;
            else if (templatecounter%100 == 0)
                motionQueryLength = 30 + 30 * (templatecounter/100);
            gesturefiles();
            getInput();
            compute();
            recognize();

            currentInputIndex = motionQueryLength;

            time = (new TimeSpan(DateTime.Now.Ticks)).TotalMilliseconds;

            done = false;
            start = true;
        }

        if (start)
        {
            compute();
            recognize();
            File.AppendAllText("Time.txt", (((new TimeSpan(DateTime.Now.Ticks)).TotalMilliseconds) - time).ToString() + " ");
            time = (new TimeSpan(DateTime.Now.Ticks)).TotalMilliseconds;
            
            start = false;
            newGes = true;
        }

        if(newGes)
        {
            updateinput();
            time = (new TimeSpan(DateTime.Now.Ticks)).TotalMilliseconds;
            newGes = false;
            start = true;
        }

        if(templatecounter%100 == 0)
        {
            File.AppendAllText("Time.txt", "\n");
            done = true;
            start = false;
        }

        if (templatecounter >= 400)
        {
            File.AppendAllText("Time.txt", "\n");
            Debug.LogError("DONE");
        }
    }
}


