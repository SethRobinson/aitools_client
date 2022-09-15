//This source from https://github.com/artbobrov/ConvolutionFilter and modified by Seth A. Robinson to work with Unity's Texture2D
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using System;
using UnityEngine;
namespace ConvFilter {

    public class ConvolutionProcessor 
    {

        public Texture2D m_originalMap;

        public ConvolutionProcessor(Texture2D bitmap) 
        {
            m_originalMap = bitmap;
        }

        public IEnumerator ComputeWith(Filter filter)
        {
            var result = new Texture2D(m_originalMap.width, m_originalMap.height, TextureFormat.RGBA32, false);
            var offset = filter.Size / 2;
            int takeABreakEveryNX = m_originalMap.width / 60;

            for (int x = 0; x < m_originalMap.width; x++) 
            {

                if (x% takeABreakEveryNX == 0)
                {
                    yield return null;
                }
                for (int y = 0; y < m_originalMap.height; y++) 
                {
                    var colorMap = new Color[filter.Size, filter.Size];

                    for (int filterY = 0; filterY < filter.Size; filterY++) 
                    {
                        int pk = (filterY + x - offset <= 0) ? 0 :
                            (filterY + x - offset >= m_originalMap.width - 1) ? m_originalMap.width - 1 : filterY + x - offset;
                        for (int filterX = 0; filterX < filter.Size; filterX++)
                        {
                            int pl = (filterX + y - offset <= 0) ? 0 :
                                (filterX + y - offset >= m_originalMap.height - 1) ? m_originalMap.height - 1 : filterX + y - offset;

                            colorMap[filterY, filterX] = m_originalMap.GetPixel(pk, pl);
                        }
                    }
              
                    result.SetPixel(x, y, colorMap * filter);
                }
            }

            m_originalMap = result;
            yield return null;
        }

        /*
        //theaded version. For no threads, use the version above
        public Texture2D ComputeWith(Filter filter, int threadsCount)
        {
            if (threadsCount == 0)
                throw new ArgumentException("Thread count shouldn't be zero");

            var result = new Texture2D(m_originalMap.width, m_originalMap.height);
            var threads = new List<Thread>();

            for (int i = 0, start = 0; i < threadsCount; i++)
            {
                var size = (m_originalMap.width - i + threadsCount - 1) / threadsCount;
                var it = start;

                var thread = new Thread(delegate () 
                {
                    Calculate(it, it + size, filter, result);
                });

                thread.Start();
                threads.Add(thread);

                start += size;
            }

            threads.ForEach(thread => thread.Join());

            return result;
        }

        */

        private void Calculate(int start, int finish, Filter filter, Texture2D result)
        {
            var offset = filter.Size / 2;
            for (int x = start; x < finish; x++) 
            {
                for (int y = 0; y < m_originalMap.height; y++) 
                {
                    var colorMap = new Color[filter.Size, filter.Size];

                    for (int filterY = 0; filterY < filter.Size; filterY++) 
                    {
                        int pk = (filterY + x - offset <= 0) ? 0 :
                            (filterY + x - offset >= m_originalMap.width - 1) ? m_originalMap.width - 1 : filterY + x - offset;

                        for (int filterX = 0; filterX < filter.Size; filterX++) 
                        {
                            int pl = (filterX + y - offset <= 0) ? 0 :
                                (filterX + y - offset >= m_originalMap.height - 1) ? m_originalMap.height - 1 : filterX + y - offset;

                            colorMap[filterY, filterX] = m_originalMap.GetPixel(pk, pl);
                        }
                    }

                    result.SetPixel(x, y, colorMap * filter);
                }
            }
        }

    }

}