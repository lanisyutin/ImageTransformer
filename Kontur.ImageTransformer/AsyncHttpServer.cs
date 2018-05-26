using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;

public enum Transform { rotate_cw, rotate_ccw, flip_v, flip_h };

namespace ImageExtensions
{
    public static class ImageExtension
    {
        public static Image CropImage(this Image img, Rectangle cropArea)
        {
            Bitmap bmpImage = new Bitmap(img);
            cropArea.Intersect(new Rectangle(0, 0, img.Width, img.Height));
            return (Image)bmpImage.Clone(cropArea, bmpImage.PixelFormat);
        }
        public static Image ApplyTransform(this Image img, string transform)
        {
            Bitmap bmpImage = new Bitmap(img);
            switch (transform)
            {
                case "rotate-cw":
                    bmpImage.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    break;
                case "rotate-ccw":
                    bmpImage.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    break;
                case "flip-v":
                    bmpImage.RotateFlip(RotateFlipType.RotateNoneFlipY);
                    break;
                case "flip-h":
                    bmpImage.RotateFlip(RotateFlipType.RotateNoneFlipX);
                    break;
                    //default:
            }

            return (Image)bmpImage;

        }
    }

}




namespace Kontur.ImageTransformer
{
    using ImageExtensions;

    internal class AsyncHttpServer : IDisposable
    {
        public AsyncHttpServer()
        {
            listener = new HttpListener();
        }

        public void Start(string prefix)
        {
            lock (listener)
            {
                if (!isRunning)
                {
                    listener.Prefixes.Clear();
                    listener.Prefixes.Add(prefix);
                    listener.Start();

                    listenerThread = new Thread(Listen)
                    {
                        IsBackground = true,
                        Priority = ThreadPriority.Highest
                    };
                    listenerThread.Start();

                    isRunning = true;
                }
            }
        }

        public void Stop()
        {
            lock (listener)
            {
                if (!isRunning)
                    return;

                listener.Stop();

                listenerThread.Abort();
                listenerThread.Join();

                isRunning = false;
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            Stop();

            listener.Close();
        }

        private void Listen()
        {
            //var cts = new CancellationTokenSource();
            //var sem = new Semaphore(2, 2);
            while (true)
            {

                try
                {
                    if (listener.IsListening)
                    {
                        var context = listener.GetContext();
                        //bool result =
                        Task.Run(() => HandleContextAsync(context));//.Wait(1000);
                                                                    //if (!result)
                                                                    //{
                                                                    //    context.Response.StatusCode = 429;
                                                                    //    context.Response.Close();
                                                                    //}

                    }
                    else Thread.Sleep(0);
                }
                catch (ThreadAbortException)
                {
                    return;
                }
                catch (Exception error)
                {
                    Console.WriteLine(error.Message);
                    // TODO: log errors
                }
            }
        }





        //public static Image ApplyTransform(Image img, string transform)
        //{
        //    Bitmap bmpImage = new Bitmap(img);
        //    switch (transform)
        //    {
        //        case "rotate-cw":
        //            bmpImage.RotateFlip(RotateFlipType.Rotate90FlipNone);
        //            break;
        //        case "rotate-ccw":
        //            bmpImage.RotateFlip(RotateFlipType.Rotate270FlipNone);
        //            break;
        //        case "flip-v":
        //            bmpImage.RotateFlip(RotateFlipType.RotateNoneFlipY);
        //            break;
        //        case "flip-h":
        //            bmpImage.RotateFlip(RotateFlipType.RotateNoneFlipX);
        //            break;
        //            //default:
        //    }

        //    return (Image)bmpImage;

        //}



        private async Task HandleContextAsync(HttpListenerContext listenerContext)
        {
            HttpListenerResponse response = listenerContext.Response;
            HttpListenerRequest request = listenerContext.Request;
            await Task.Run(() =>
            {
                //response.ContentType = request.ContentType; //возможно стоит вернуть после отладки
                response.ContentEncoding = request.ContentEncoding;

                string[] split = request.RawUrl.Split(new Char[] { '/', ',' });
                string transform = split[2];
                if (!transform.Equals("rotate-cw") && !transform.Equals("rotate-ccw") && !transform.Equals("flip-h") && !transform.Equals("flip-v"))
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    response.Close();
                }
                try
                {
                    int x = Convert.ToInt32(split[3]);
                    int y = Convert.ToInt32(split[4]);
                    int h = Convert.ToInt32(split[5]);
                    int w = Convert.ToInt32(split[6]);

                    if (w < 0)
                    {
                        x -= -w;
                        w = -w;
                    }
                    if (h < 0)
                    {
                        y -= -h;
                        h = -h;
                    }

                    using (var image = Image.FromStream(request.InputStream))
                    {

                        if (image.Height > 1000 || image.Width > 1000 || request.ContentLength64 > 100 * 1024)
                        {
                            response.StatusCode = (int)HttpStatusCode.BadRequest;
                            response.Close();
                        }

                        var transformedimage = image.ApplyTransform(transform);

                        transformedimage = transformedimage.CropImage(new Rectangle(x, y, w, h));
                        transformedimage.Save(response.OutputStream, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                catch (ArgumentException e) when (e.Message == "Длина или ширина прямоугольника '{X=0,Y=0,Width=0,Height=0}' не может равняться 0.")
                {
                    response.StatusCode = (int)HttpStatusCode.NoContent;
                    response.Close();
                }
                catch (ArgumentException e) when (e.Message == "Недопустимый параметр.")
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    response.Close();
                }
                catch (FormatException e) when (e.Message == "Входная строка имела неверный формат.")
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    response.Close();
                }
                catch
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    response.Close();
                }

                response.StatusCode = (int)HttpStatusCode.OK;
                response.Close();


            });

        }

        private readonly HttpListener listener;

        private Thread listenerThread;
        private bool disposed;
        private volatile bool isRunning;
    }

}