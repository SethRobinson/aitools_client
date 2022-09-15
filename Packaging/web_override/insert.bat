echo Disabled the sed replacement command, I don't use it anymore

:sed '/UnityLoader.instantiate/ r index_insert.txt' index.html > temp.txt

:copy temp.txt index.html
:del temp.txt
