<!-- Simple slide shower viewer by Seth A. Robinson V1.00

  * Displays all jpg/png that exist in the same folder
  * Displays them in alphabetical order
  * Auto-preloads the next image in the sequence to help make things snappy

 -->

<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Seth's AI Tools Slideshow</title>
   
    <style>
        body {
            background-color: #f4f4f4;
            font-family: Arial, sans-serif;
           
        }
        .cycle-slideshow {
    width: 100%;
    max-height: 80vh; /* limiting to 80% of viewport height */
    position: relative;
    overflow: hidden; /* Hides the overflowing content */
    margin-bottom: 20px;
}
       
        .cycle-slideshow img.active {
            display: block;
        }
        .center {
            text-align: center;
            margin-bottom: 10px;
        }
        #prev, #next {
            background-color: #007BFF;
            padding: 10px 20px;
            color: #ffffff;
            text-decoration: none;
            border-radius: 5px;
            margin: 5px;
            transition: background-color 0.3s;
        }
        #prev:hover, #next:hover {
            background-color: #0056b3;
        }
        .cycle-slideshow div.slide {
    display: none;
    width: 100%;
    height: 80vh; /* 80% of viewport height */
    background-size: contain; /* Image will scale to fit */
    background-repeat: no-repeat; /* Image won't repeat */
    background-position: center; /* Image will be centered */
}
    .cycle-slideshow div.slide.active {
        display: block;
    }

        </style>
</head>
<body>
    <div class="cycle-slideshow">
    <?php
$directory = "./";
$images = glob($directory . "/*.{jpg,png}", GLOB_BRACE);
sort($images);

foreach ($images as $image) {
    echo '<div class="slide" style="background-image: url(' . $image . ')"></div>';
}
?>
    </div>

    <div class="center">
        <a href="javascript:void(0)" id="prev">Prev</a>
        <a href="javascript:void(0)" id="next">Next</a>
    </div>
    <div class="center" id="image-counter"></div>

   <script>
      var slides = document.querySelectorAll('.cycle-slideshow .slide');
      var currentIndex = 0;
      
      function preloadImage(index) {
          // Preload the image at the specified index
          new Image().src = slides[index].style.backgroundImage.slice(5, -2);
      }
      
      function showImage(index) {
          slides.forEach(slide => slide.classList.remove('active'));
          slides[index].classList.add('active');
          document.getElementById('image-counter').innerText = `(${index + 1} of ${slides.length})`;

          // Preload the next image
          var nextIndex = index + 1;
          if (nextIndex >= slides.length) {
              nextIndex = 0;
          }
          preloadImage(nextIndex);
      }

      document.getElementById('prev').addEventListener('click', function () {
          currentIndex--;
          if (currentIndex < 0) {
              currentIndex = slides.length - 1;
          }
          showImage(currentIndex);
      });

      document.getElementById('next').addEventListener('click', function () {
          currentIndex++;
          if (currentIndex >= slides.length) {
              currentIndex = 0;
          }
          showImage(currentIndex);
      });

      // Show the first image and preload the next one
      showImage(currentIndex);
</script>
</body>
</html>