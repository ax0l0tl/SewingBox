﻿#Requires -Modules MyPsModule

function TestLibFuncGit {
  param (
        [string]$StringParam,
        [string]$StringParam2,
        [string]$StringParam3,
        [Int]$IntParam,
        [string]$Result
    )
    Write-Host "Called with $StringParam and $IntParam and $Result"

    Write-Host $Result
    $SrxEnv.ResultMessage = $Result
}

function QRY_TestQueryGit {
  param (
        [string]$StringParam,        
        [Int]$IntParam
    )
    Write-Host "Called with $StringParam and $IntParam"
   
    for ($i = 0; $i -lt $IntParam; $i++) {
        $SRXEnv.ResultList.Add("$StringParam $i")
    }
}