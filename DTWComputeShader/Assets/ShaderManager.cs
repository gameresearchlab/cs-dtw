/*
 * This is an example apparatus for DTW on compute shader. Inputs and templates could be loaded by putting them into folders (default or custom, just change the name). 
 */

using UnityEngine;
using System.Collections;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

public class ShaderManager : MonoBehaviour
{
    //Number of Templates. This and the number of threads needs to be the same to run.
    static private int numOfTemplates = 10;

    //Length of template.
    static private int motionTemplateLength;

    //Length of Query/Input
    static private int motionQueryLength;

    //Number of bodyparts
    static private int numOfBodyParts = 6;

    static private int minindex = 0;

    static private string[] allGestures = new string[0];
    static private string[] input = new string[0];
    static private string[] gestures;

//====================================================Compute shader stuff==================================================================
    static public int kiCalc;                           

    static public ComputeBuffer costMatrix;         
    static public ComputeBuffer globalCostMatrix;			
    static public ComputeBuffer motionTemplate;          
    static public ComputeBuffer motionQuery;     
    static public ComputeShader _shader;				

    static float[] costMatrixArray = new float[numOfBodyParts * numOfTemplates * motionTemplateLength * motionQueryLength];
    static float[] globalCostMatrixArray = new float[numOfBodyParts * numOfTemplates * motionTemplateLength * motionQueryLength];

    static Quaternion[] motionTemplateArray = new Quaternion[numOfBodyParts * numOfTemplates * motionTemplateLength];

    static Quaternion[] motionQueryArray = new Quaternion[numOfBodyParts * motionQueryLength];

    static public ComputeBuffer shaderMin;
    static float[] shaderMinArray = new float[numOfTemplates * numOfBodyParts * 2];

    static public ComputeBuffer allMin;
    static int[] allMinArray = new int[numOfTemplates * numOfBodyParts];

    static ComputeBuffer dynamicTemplate;
    static int[] dynamicTemplateArray = new int[numOfBodyParts * numOfTemplates + 1];

    static ComputeBuffer sum;
    static int[] sumArray = new int[numOfBodyParts * numOfTemplates + 1];
//===========================================================================================================================================

    static bool gestureGo = false;
    string[] tb;
    public List<Quaternion> inputGesture = new List<Quaternion>();
    static int currentInputIndex = motionQueryLength;
    bool start = true;
    bool done = true;

    //these arrays are for dynamic length input
    static int[] inputArrayCount = new int[numOfBodyParts * numOfTemplates + 1];
    static int[] inputArraySum = new int[numOfBodyParts * numOfTemplates + 1];


    static int[] bodypartsmin = new int[numOfBodyParts];
    static int[] mincounter = new int[numOfTemplates];


    static int stopindex = 100000;
    static int templatecounter = 0;
    static int jumpin = 0;
    static double time;

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


        _shader.Dispatch(kiCalc, 10, 1, 1);


        globalCostMatrix.GetData(globalCostMatrixArray);
        costMatrix.GetData(costMatrixArray);


        shaderMin.GetData(shaderMinArray);
        allMin.GetData(allMinArray);
    }

    //function returns matched template, min and 'error'.
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
            Debug.Log("Matched template is: " + (int)(Math.Ceiling((double)counter / 6)) + " " + shaderMinArray[2 * (counter - 1)] + " " + shaderMinArray[2 * (counter - 1) + 1] + " " + currentInputIndex);

        }

        while (tindex > sumArray[counter])
        {
            
            if ((tindex) < sumArray[counter + 1])
            {
                Debug.Log("Matched template is: " + (int)(Math.Ceiling((double)counter / 6)) + " " + " " + shaderMinArray[2 * (counter)] + " " + shaderMinArray[2 * (counter) + 1] + " " + currentInputIndex);

                break;
            }
            counter++;
        }
    }

    //reads all file in Template folder and save all lines into array
    void gesturefiles()
    {
        var FileNames = Directory.GetFiles(Application.dataPath + "/Template", "*.txt");
        
        Array.Sort(FileNames);

        int newsize = allGestures.Length;
        string[] tb;

        if (FileNames == null)
        {
            Debug.Log("Didn't find any files");
            return;
        } // if

        int tcounter = 0;
        foreach (string str in FileNames)
        {
            Debug.Log(str);
            gestures = File.ReadAllLines(str);

            //these two buffers are used for dynamic length templates
            dynamicTemplateArray[tcounter + 1] = gestures.Length;
            sumArray[tcounter + 1] = sumArray[tcounter] + dynamicTemplateArray[tcounter + 1];

            //get the size of the array and put it in as template length here
            Array.Resize<string>(ref allGestures, newsize + gestures.Length);


            for (int i = 0; i < gestures.Length; i++)
            {
                allGestures[sumArray[tcounter] + i] = gestures[i];
            }
            tcounter++;
            newsize = allGestures.Length;

        } // foreach

        motionTemplateArray = new Quaternion[allGestures.Length];

        for (int i = 0; i < allGestures.Length; i++)
        {
            tb = allGestures[i].Split(',');
            Quaternion q = new Quaternion(float.Parse(tb[0]),float.Parse(tb[1]),float.Parse(tb[2]),float.Parse(tb[3]));
            
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

    //reads all files in InputGestures folder and save all lines into array
    void getInput()
    {
        var FileNames = Directory.GetFiles(Application.dataPath + "/InputGestures", "*.txt");

        Array.Sort(FileNames);

        if (FileNames == null)
        {
            Debug.Log("Didn't find any files");
            return;
        } // if

        int newsize = input.Length;
        int tcounter = 0;

        foreach (string str in FileNames)
        {
            gestures = File.ReadAllLines(str);

            inputArrayCount[tcounter+1] = gestures.Length;
            inputArraySum[tcounter + 1] = inputArraySum[tcounter] + gestures.Length;
            Array.Resize<string>(ref input, newsize + gestures.Length);

            for (int i = 0; i < gestures.Length; i++)
            {
                input[inputArraySum[tcounter] + i] = gestures[i];
            }

            tcounter++;
            newsize = input.Length;
        }
        

        motionQueryArray = new Quaternion[numOfBodyParts * motionQueryLength];
        costMatrixArray = new float[allGestures.Length * motionQueryLength];
        globalCostMatrixArray = new float[allGestures.Length * motionQueryLength];

        Array.Clear(motionQueryArray, 0, motionQueryArray.Length);
        for (int j = 0; j < numOfBodyParts; j++)
        {
            for (int i = 0; i < motionQueryLength; i++)
            {
                tb = input[inputArraySum[j] + i].Split(',');
                Quaternion q = new Quaternion(float.Parse(tb[0]), float.Parse(tb[1]), float.Parse(tb[2]), float.Parse(tb[3]));
                
                inputGesture.Add(q);
                motionQueryArray[j*motionQueryLength + i] = q;
            }
       }

        for(int i = 1; i < inputArrayCount.Length; i++)
        {
            if (stopindex > inputArrayCount[i] && inputArrayCount[i] > 50)
                stopindex = inputArrayCount[i];
        }

        motionQuery = new ComputeBuffer(motionQueryArray.Length, 4*4);
        motionQuery.SetData(motionQueryArray);


        globalCostMatrix = new ComputeBuffer(globalCostMatrixArray.Length, 4);
        globalCostMatrix.SetData(globalCostMatrixArray);

        costMatrix = new ComputeBuffer(costMatrixArray.Length, 4);
        costMatrix.SetData(costMatrixArray);

    }

    //copy input from list to query array 
    void inputingGesture()
    {

            for (int i = 0; i < motionQueryLength*numOfBodyParts; i++)
            {
                motionQueryArray[i] = inputGesture[i];
            }

        motionQuery.SetData(motionQueryArray);
    }


    void Update()
    {
        //this if statement will fill the template buffer and query buffer and run dtw for however long motionQueryLength is.
        if (done)
        {
            //this is queryLength. change it to however long a bone/bodypart is supposed to be
            motionQueryLength = 60;
            gesturefiles();
            getInput();
            compute();
            recognize();
            currentInputIndex = motionQueryLength;

            done = false;
            start = true;
        }

        //this if statement will loop through the entire input frame by frame and perform dtw
        if (start)
        {
            if (inputGesture.Count == motionQueryArray.Length)
            {
                gestureGo = true;
            }

            //put the next frame into list for all bones
            for (int i = 0; i < numOfBodyParts; i++)
            {
                tb = input[inputArraySum[i + templatecounter*numOfBodyParts] + currentInputIndex].Split(',');
                Quaternion q = new Quaternion(float.Parse(tb[0]),float.Parse(tb[1]), float.Parse(tb[2]), float.Parse(tb[3]));
                inputGesture.Insert((i + 1) * (motionQueryLength)+i, q);
            }
            currentInputIndex++;

            //remove the first element from list
            if (inputGesture.Count > motionQueryLength)
            {
                for (int i = 0; i < numOfBodyParts; i++)
                {
                    inputGesture.RemoveAt(i * (motionQueryLength));
                }
            }
            

            if (gestureGo)
            {
                //put items from list into query buffer
                inputingGesture();
                //pass it to shader for dtw
                compute();
                //find matching template
                recognize();
                
                gestureGo = false;
            }

            
            if(currentInputIndex == inputArrayCount[1])
            {
                Debug.LogError("Done");
                currentInputIndex = 0;
            }
            
        }
    }
}


