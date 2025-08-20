using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Render.Commands;

namespace PdfLib.Render
{
    /// <summary>
    /// The purpose of this interface is to allow a caller control
    /// of execution of "inner commands" (inside XForm objects for
    /// instance). Useful if one want to abort mid execution (say
    /// if the user switches to a new page)
    /// </summary>
    public interface IExecutor
    {
        void Execute(object cmds, IDraw renderer);
        void Execute(IExecutable cmds, IDraw renderer);
    }

    /// <summary>
    /// Standar interface for drawable objects
    /// </summary>
    public interface IExecutable
    {
        
    }

    internal interface IExecutableImpl : IExecutable
    {
        RenderCMD[] Commands { get; }
    }

    internal class StdExecutor : IExecutor
    {
        public bool Running = true;

        public static readonly StdExecutor STD = new StdExecutor();

        private StdExecutor() { }

        void IExecutor.Execute(IExecutable cmds, IDraw renderer)
        {
            Execute(((IExecutableImpl)cmds).Commands, renderer);
        }

        void IExecutor.Execute(object cmds, IDraw renderer)
        {
            Execute((RenderCMD[])cmds, renderer);
        }

        private void Execute(RenderCMD[] cmds, IDraw renderer)
        {
            for (int c = 0; c < cmds.Length && Running; c++)
                cmds[c].Execute(renderer);
        }
    }
}
