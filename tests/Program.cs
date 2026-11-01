using System;
using FriendsWine;

static class Program
{
    static int assertions;
    static void Near(float actual, double expected, string message, double tolerance = 2e-5)
    {
        assertions++;
        if (Math.Abs(actual - expected) > tolerance) throw new Exception(message + ": got " + actual + ", expected " + expected);
    }
    static StretchScale At(double beat)
    { return BeatMath.Evaluate(beat * BeatMath.SecondsPerBeat, .6f, 1.8f, 1.5f, .7f); }
    static void Main()
    {
        // Different values on all four axes catch swapped or incorrectly coupled settings.
        double[,] samples = { {0,1,1}, {.125,.8,1.4}, {.25,.6,1.8}, {.375,.8,1.4}, {.5,1,1}, {.625,1.25,.85}, {.75,1.5,.7}, {.875,1.25,.85}, {1,1,1} };
        for (int i = 0; i < samples.GetLength(0); i++)
        { var s = At(samples[i,0]); Near(s.X,samples[i,1],"X at " + samples[i,0]); Near(s.Y,samples[i,2],"Y at " + samples[i,0]); }
        // Continuity at every segment boundary and long-running unscaled clock stability.
        for (int i = 1; i <= 4; i++)
        { var a = At(i*.25-1e-7); var b = At(i*.25+1e-7); Near(a.X,b.X,"X continuity"); Near(a.Y,b.Y,"Y continuity"); }
        foreach (double offset in new[] { 1.0, 1300, 100000 })
            foreach (double phase in new[] { .125,.25,.5,.75,.875 })
            { var a=At(phase); var b=At(offset+phase); Near(a.X,b.X,"X repeat"); Near(a.Y,b.Y,"Y repeat"); }
        foreach (double invalid in new[] { -1.0, double.NaN, double.PositiveInfinity })
        { var s=BeatMath.Evaluate(invalid,.5f,2,2,.5f); Near(s.X,1,"invalid elapsed X"); Near(s.Y,1,"invalid elapsed Y"); }
        foreach (float value in new[] { 0f,-1,float.NaN,float.PositiveInfinity })
        { assertions++; if(BeatMath.ValidFactor(value)) throw new Exception("Invalid factor accepted"); }
        assertions++; if(!BeatMath.ValidFactor(101)) throw new Exception("Positive scale must not be arbitrarily capped");
        // A negative vertical displacement is expected below the anchor; no cumulative state.
        for(int frame=0;frame<100000;frame++)
        {
            var s=BeatMath.Evaluate(frame/144.0,.6f,1.8f,1.5f,.7f);
            if(s.X<.59999f||s.X>1.50001f||s.Y<.69999f||s.Y>1.80001f) throw new Exception("Scale drifted");
        }
        assertions++;
        Console.WriteLine("PASS: " + assertions + " checks, including 100000 frames without scale drift.");
    }
}
