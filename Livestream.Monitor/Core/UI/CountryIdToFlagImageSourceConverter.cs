/*
 * Created by Drew Noakes
 * 20 May 2009
 * http://drewnoakes.com
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Livestream.Monitor.Core.UI
{
    /// <summary>
    /// Provides an image source for the flag of a country, as specified via the country's two letter ISO code.
    /// </summary>
    /// <remarks>
    /// Flag images used by this converter have been provided by FamFamFam and are publicaly available at:
    /// http://www.famfamfam.com/lab/icons/flags/
    /// </remarks>
    [ValueConversion(typeof(string), typeof(BitmapImage))]
    public sealed class CountryIdToFlagImageSourceConverter : IValueConverter
    {
        private const string AllCountryIdsString 
            = "ad,ae,af,ag,ai,al,am,an,ao,ar,as,at,au,aw,ax,az,ba,bb,bd,be,bf,bg,bh,bi,bj,bm,bn,bo,br,bs,bt,bv,bw,by,"
            + "bz,ca,catalonia,cc,cd,cf,cg,ch,ci,ck,cl,cm,cn,co,cr,cs,cu,cv,cx,cy,cz,de,dj,dk,dm,do,dz,ec,ee,eg,eh,en,"
            + "er,es,et,eu,fi,fj,fk,fm,fo,fr,ga,gb,gd,ge,gf,gg,gh,gi,gl,gm,gn,gp,gq,gr,gs,gt,gu,gw,gy,hk,hm,hn,hr,ht,hu,"
            + "id,ie,il,in,io,iq,ir,is,it,jm,jo,jp,ke,kg,kh,ki,km,kn,kp,kr,kw,ky,kz,la,lb,lc,li,lk,lr,ls,lt,lu,lv,ly,ma,"
            + "mc,md,me,mg,mh,mk,ml,mm,mn,mo,mp,mq,mr,ms,mt,mu,mv,mw,mx,my,mz,na,nc,ne,nf,ng,ni,nl,no,np,nr,nu,nz,om,pa,"
            + "pe,pf,pg,ph,pk,pl,pm,pn,pr,ps,pt,pw,py,qa,re,ro,rs,ru,rw,sa,sb,sc,scotland,sd,se,sg,sh,si,sj,sk,sl,sm,sn,"
            + "so,sr,st,sv,sy,sz,tc,td,tf,tg,th,tj,tk,tl,tm,tn,to,tr,tt,tv,tw,tz,ua,ug,um,us,uy,uz,va,vc,ve,vg,vi,vn,vu,"
            + "wales,wf,ws,ye,yt,za,zm,zw";

        /// <summary>
        /// Returns an enumerable set of country ids supported by this set of flags.
        /// </summary>
        public static IEnumerable<string> AllCountryIds
        {
            get
            {
                int startIndex = 0;
                int endIndex;
                while ((endIndex = AllCountryIdsString.IndexOf(',', startIndex)) != -1)
                {
                    yield return AllCountryIdsString.Substring(startIndex, endIndex - startIndex);
                    startIndex = endIndex + 1;
                }
            }
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var countryId = value as string;

            if (countryId == null)
                return null;

            try
            {
                var path = $"/Livestream.Monitor;component/Images/CountryFlags/{countryId.ToLower()}.png";
                var uri = new Uri(path, UriKind.Relative);
                var resourceStream = Application.GetResourceStream(uri);
                if (resourceStream == null)
                    return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = resourceStream.Stream;
                bitmap.EndInit();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
